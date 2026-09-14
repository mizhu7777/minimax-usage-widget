using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.Services;

namespace MiniMaxUsage.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly string _projectRoot;
    private readonly UsageCacheReader _cacheReader;
    private readonly HistoryReader _historyReader;
    private readonly Func<DateTimeOffset> _clock;

    private TrendRange _selectedRange = TrendRange.Days7;
    private bool _isRefreshing;
    private string _plan = "";
    private double _fiveHourPercent;
    private double _weeklyPercent;
    private QuotaSeverity _fiveHourSeverity;
    private QuotaSeverity _weeklySeverity;
    private string _fiveHourResetText = "";
    private string _weeklyResetText = "";
    private string _lastUpdatedText = "";
    private string _statusText = "正在加载…";
    private QuotaSeverity _statusSeverity = QuotaSeverity.Normal;
    private string _errorMessage = "";
    private IReadOnlyList<HistorySample> _allSamples = [];
    private IReadOnlyList<TrendSegment> _fiveHourSegments = [];
    private IReadOnlyList<TrendSegment> _weeklySegments = [];
    // S1 配套修复:15 秒定时器每跳都会调 Load(),历史文件没变化时(长度+写入时间均未变)
    // 直接复用上次解析结果,避免随 history.jsonl 增长出现的周期性 UI 卡顿
    private long _historyFileLength = -1;
    private DateTime _historyFileWriteUtc = DateTime.MinValue;
    private bool _trendsDirty = true;
    // P1-7 修复:历史文件长时间静止时,趋势窗口仍需每 5 分钟强制重建一次,
    // 让 24h/7d/30d 滑动窗口与轴终点随真实时间推移
    private DateTimeOffset _lastTrendsRebuild = DateTimeOffset.MinValue;
    // P1-5 修复:趋势轴锚定 [Now-区间, Now],由 RebuildTrends 随每次重建刷新
    private DateTimeOffset _axisStart;
    private DateTimeOffset _axisEnd;

    public MainViewModel(
        string projectRoot,
        UsageCacheReader cacheReader,
        HistoryReader historyReader,
        Func<DateTimeOffset>? clock = null)
    {
        _projectRoot = projectRoot;
        _cacheReader = cacheReader;
        _historyReader = historyReader;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Plan { get => _plan; set => SetField(ref _plan, value); }
    public double FiveHourPercent { get => _fiveHourPercent; set => SetField(ref _fiveHourPercent, value); }
    public double WeeklyPercent { get => _weeklyPercent; set => SetField(ref _weeklyPercent, value); }
    public QuotaSeverity FiveHourSeverity { get => _fiveHourSeverity; set => SetField(ref _fiveHourSeverity, value); }
    public QuotaSeverity WeeklySeverity { get => _weeklySeverity; set => SetField(ref _weeklySeverity, value); }
    public string FiveHourResetText { get => _fiveHourResetText; set => SetField(ref _fiveHourResetText, value); }
    public string WeeklyResetText { get => _weeklyResetText; set => SetField(ref _weeklyResetText, value); }
    public string LastUpdatedText { get => _lastUpdatedText; set => SetField(ref _lastUpdatedText, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public QuotaSeverity StatusSeverity { get => _statusSeverity; set => SetField(ref _statusSeverity, value); }
    public string ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }
    // P2-11 修复:删 HasHistory 死字段(XAML 无消费,TrendChart 自己判断空态)
    public bool IsRefreshing { get => _isRefreshing; set => SetField(ref _isRefreshing, value); }

    // P1-8 修复:Range 按钮选中态用显式 bool 绑到 Tag,Style 内的 DataTrigger 切换样式
    public bool IsRange24h { get => _selectedRange == TrendRange.Hours24; }
    public bool IsRange7d { get => _selectedRange == TrendRange.Days7; }
    public bool IsRange30d { get => _selectedRange == TrendRange.Days30; }

    public TrendRange SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (SetField(ref _selectedRange, value))
            {
                OnPropertyChanged(nameof(IsRange24h));
                OnPropertyChanged(nameof(IsRange7d));
                OnPropertyChanged(nameof(IsRange30d));
                _trendsDirty = true;
                RebuildTrends();
            }
        }
    }

    public IReadOnlyList<TrendSegment> FiveHourSegments
    {
        get => _fiveHourSegments;
        set => SetField(ref _fiveHourSegments, value);
    }

    public IReadOnlyList<TrendSegment> WeeklySegments
    {
        get => _weeklySegments;
        set => SetField(ref _weeklySegments, value);
    }

    // P1-5 修复:趋势轴窗口,供 TrendChart 锚定 X 轴
    public DateTimeOffset AxisStart
    {
        get => _axisStart;
        private set => SetField(ref _axisStart, value);
    }

    public DateTimeOffset AxisEnd
    {
        get => _axisEnd;
        private set => SetField(ref _axisEnd, value);
    }

    public void Load()
    {
        var now = _clock();
        LoadCache(now);
        // P1-7 修复:Load() 内部统一读一次 history,然后传给 RebuildTrends,
        // 避免之前 LoadHistory 读完丢弃 + RebuildTrends 再读一次的双重 IO
        var historyPath = Path.Combine(_projectRoot, ".cache", "history.jsonl");
        var fileInfo = new FileInfo(historyPath);
        var unchanged = fileInfo.Exists
            && fileInfo.Length == _historyFileLength
            && fileInfo.LastWriteTimeUtc == _historyFileWriteUtc;
        if (!unchanged)
        {
            // P1-8 修复:Read 与采集器竞争时返回 null(而非抛异常),保留上次数据待下个周期重试
            var read = _historyReader.Read(historyPath, now.AddDays(-90));
            if (read is not null)
            {
                _allSamples = read;
                _historyFileLength = fileInfo.Exists ? fileInfo.Length : -1;
                _historyFileWriteUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
                _trendsDirty = true;
            }
        }

        // P1-7 修复:即使历史静止,每 5 分钟也强制重建一次,滑动窗口与轴终点才会前移
        if (now - _lastTrendsRebuild >= TimeSpan.FromMinutes(5))
            _trendsDirty = true;

        RebuildTrends();
    }

    private void LoadCache(DateTimeOffset now)
    {
        var cachePath = Path.Combine(_projectRoot, ".cache", "cache.json");
        var result = _cacheReader.Read(cachePath, now);

        if (!result.IsSuccess)
        {
            Plan = "MiniMax TokenPlan";
            FiveHourPercent = 0;
            WeeklyPercent = 0;
            FiveHourSeverity = QuotaSeverity.Normal;
            WeeklySeverity = QuotaSeverity.Normal;
            FiveHourResetText = "";
            WeeklyResetText = "";
            LastUpdatedText = "";
            StatusText = "缓存不可用";
            StatusSeverity = QuotaSeverity.Critical;
            ErrorMessage = result.FatalError ?? "无法读取缓存数据。";
            return;
        }

        var usage = result.Value!;
        Plan = usage.Plan;
        FiveHourPercent = usage.FiveHour.RemainingPercent;
        WeeklyPercent = usage.Weekly.RemainingPercent;
        FiveHourSeverity = QuotaSeverityRules.FromPercent(FiveHourPercent);
        WeeklySeverity = QuotaSeverityRules.FromPercent(WeeklyPercent);
        // P2-13 修复:有精确 reset_at 时按当前时钟动态计算倒计时(每 15 秒 Load 刷新),
        // 替代采集时刻写入缓存的静态文本;无 reset_at(降级数据)时回退缓存文本
        FiveHourResetText = FormatResetCountdown(usage.FiveHour.ResetAt, now, usage.FiveHour.ResetText);
        WeeklyResetText = FormatResetCountdown(usage.Weekly.ResetAt, now, usage.Weekly.ResetText);
        LastUpdatedText = $"最后更新: {usage.LastUpdate.ToLocalTime():HH:mm}";

        // P1-12 + N3 修复:ErrorMessage 的覆盖/清空规则:
        // - 缓存 status=error 且有 error 字段 → 设置 ErrorMessage
        // - 缓存 status=ok → 清空 ErrorMessage(成功覆盖之前的错误)
        // 避免"成功刷新后旧错误横幅永久残留"
        if (usage.Error is not null)
            ErrorMessage = usage.Error;
        else
            ErrorMessage = "";

        if (usage.IsStale)
        {
            StatusText = "数据已过期";
            StatusSeverity = QuotaSeverity.Warning;
        }
        else if (usage.Error is not null)
        {
            StatusText = "最近一次采集失败";
            StatusSeverity = QuotaSeverity.Warning;
        }
        else
        {
            StatusText = "数据正常";
            StatusSeverity = QuotaSeverity.Normal;
        }
    }

    private void RebuildTrends()
    {
        // P1-7 + P2-9 修复:用 _allSamples 缓存,不再重复 IO;
        // TrendSeriesBuilder 内部会按 selectedRange 过滤,无需在 HistoryReader 收窄
        // S1 配套修复:数据与区间都没变时跳过重建 —— 否则每 15 秒都会生成新集合实例,
        // 触发 TrendChart(AffectsRender)对最多数万点的全量重绘
        if (!_trendsDirty)
            return;
        var now = _clock();
        FiveHourSegments = TrendSeriesBuilder.Build(_allSamples, _selectedRange, now, s => s.FiveHourPercent);
        WeeklySegments = TrendSeriesBuilder.Build(_allSamples, _selectedRange, now, s => s.WeeklyPercent);
        // P1-5 修复:轴窗口锚定为 [Now-区间, Now]
        AxisEnd = now;
        AxisStart = now - TrendDuration(_selectedRange);
        _trendsDirty = false;
        _lastTrendsRebuild = now;
    }

    private static TimeSpan TrendDuration(TrendRange range) => range switch
    {
        TrendRange.Hours24 => TimeSpan.FromHours(24),
        TrendRange.Days7 => TimeSpan.FromDays(7),
        TrendRange.Days30 => TimeSpan.FromDays(30),
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    /// <summary>
    /// P2-13 修复:按当前时钟把 reset_at 换算为倒计时文本,格式与采集脚本 Format-ResetText 一致;
    /// 已过期返回"即将重置",无 reset_at 时回退缓存静态文本。
    /// </summary>
    public static string FormatResetCountdown(DateTimeOffset? resetAt, DateTimeOffset now, string fallback)
    {
        if (resetAt is null)
            return fallback;

        var diff = resetAt.Value - now;
        if (diff.TotalSeconds <= 0)
            return "即将重置";

        var totalMinutes = (long)Math.Floor(diff.TotalMinutes);
        if (totalMinutes <= 0)
            return "即将重置";
        if (totalMinutes < 60)
            return $"{totalMinutes}分钟后";

        var hours = totalMinutes / 60;
        var mins = totalMinutes % 60;
        if (hours < 24)
            return mins == 0 ? $"{hours}小时后" : $"{hours}小时{mins}分后";

        var days = hours / 24;
        var remHours = hours % 24;
        return remHours == 0 ? $"{days}天后" : $"{days}天{remHours}小时后";
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        try
        {
            var result = await PowerShellRefreshService.RunAsync(_projectRoot);
            if (!result.Success)
            {
                // 刷新失败先记录具体原因,Load() 后如果缓存本身有 error 字段,
                // 会被 LoadCache 覆盖;否则保留这里写的"刷新失败"消息
                ErrorMessage = result.Error ?? "刷新失败";
                StatusText = "刷新失败";
                StatusSeverity = QuotaSeverity.Warning;
            }
            Load();
        }
        catch
        {
            ErrorMessage = "刷新时发生异常";
            StatusText = "刷新异常";
            StatusSeverity = QuotaSeverity.Critical;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}