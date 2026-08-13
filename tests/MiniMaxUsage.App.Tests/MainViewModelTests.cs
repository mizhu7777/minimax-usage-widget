using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.Services;
using MiniMaxUsage.App.ViewModels;

namespace MiniMaxUsage.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void LoadsCacheAndSetsDefaultRange()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"MiniMax TokenPlan",
             "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":54,"reset_at":"2026-07-22T16:00:00Z","reset_text":"2小时后"},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":87,"reset_at":"2026-07-26T16:00:00Z","reset_text":"4天后"}]}
            """);
        temp.WriteFile(".cache/history.jsonl", "");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);
        vm.Load();

        Assert.Equal("MiniMax TokenPlan", vm.Plan);
        Assert.Equal(54, vm.FiveHourPercent);
        Assert.Equal(87, vm.WeeklyPercent);
        Assert.Equal(Models.TrendRange.Days7, vm.SelectedRange);
    }

    [Fact]
    public void MapsSeverityBoundaries()
    {
        Assert.Equal(QuotaSeverity.Normal, QuotaSeverityRules.FromPercent(21));
        Assert.Equal(QuotaSeverity.Warning, QuotaSeverityRules.FromPercent(20));
        Assert.Equal(QuotaSeverity.Critical, QuotaSeverityRules.FromPercent(10));
    }

    [Fact]
    public void ShowsStaleStatusAfterThreeMinutes()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"Plan",
             "last_update":"2026-07-22T11:56:00+08:00","error":null,"items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":50,"reset_at":"2026-07-22T16:00:00Z","reset_text":"2h"},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":80,"reset_at":"2026-07-26T16:00:00Z","reset_text":"4d"}]}
            """);
        temp.WriteFile(".cache/history.jsonl", "");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);
        vm.Load();

        Assert.Equal("数据已过期", vm.StatusText);
        Assert.Equal(QuotaSeverity.Warning, vm.StatusSeverity);
    }
}