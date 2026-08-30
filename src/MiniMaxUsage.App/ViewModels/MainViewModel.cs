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

    public void Load()
    {
        var now = _clock();
        LoadCache(now);
        // P1-7 修复:Load() 内部统一读一次 history,然后传给 RebuildTrends,
        // 避免之前 LoadHistory 读完丢弃 + RebuildTrends 再读一次的双重 IO
        var historyPath = Path.Combine(_projectRoot, ".cache", "history.jsonl");
        _allSamples = _historyReader.Read(historyPath, now.AddDays(-90));
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
        FiveHourResetText = usage.FiveHour.ResetText;
        WeeklyResetText = usage.Weekly.ResetText;
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
        var now = _clock();
        FiveHourSegments = TrendSeriesBuilder.Build(_allSamples, _selectedRange, now, s => s.FiveHourPercent);
        WeeklySegments = TrendSeriesBuilder.Build(_allSamples, _selectedRange, now, s => s.WeeklyPercent);
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