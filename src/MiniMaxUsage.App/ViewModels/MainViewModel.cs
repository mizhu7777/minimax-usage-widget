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
    private bool _hasHistory;
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
    public bool HasHistory { get => _hasHistory; set => SetField(ref _hasHistory, value); }
    public bool IsRefreshing { get => _isRefreshing; set => SetField(ref _isRefreshing, value); }

    public TrendRange SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (SetField(ref _selectedRange, value))
                RebuildTrends();
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
        LoadHistory(now);
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
        ErrorMessage = usage.Error ?? "";

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

    private void LoadHistory(DateTimeOffset now)
    {
        var historyPath = Path.Combine(_projectRoot, ".cache", "history.jsonl");
        var samples = _historyReader.Read(historyPath, now.AddDays(-90));
        HasHistory = samples.Count > 0;
        RebuildTrends();
    }

    private void RebuildTrends()
    {
        var historyPath = Path.Combine(_projectRoot, ".cache", "history.jsonl");
        var now = _clock();
        var samples = _historyReader.Read(historyPath, now.AddDays(-90));
        FiveHourSegments = TrendSeriesBuilder.Build(samples, _selectedRange, now, s => s.FiveHourPercent);
        WeeklySegments = TrendSeriesBuilder.Build(samples, _selectedRange, now, s => s.WeeklyPercent);
        HasHistory = samples.Count > 0;
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}