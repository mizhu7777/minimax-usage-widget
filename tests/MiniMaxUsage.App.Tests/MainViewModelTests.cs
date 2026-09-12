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
        // P2-15 修复:这个测试只验证默认状态保持,具体边界值已迁到 QuotaSeverityRulesTests
        // 这里保留一个烟测,避免完全空缺
        Assert.Equal(QuotaSeverity.Normal, QuotaSeverityRules.FromPercent(50));
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

    // P2-16 修复:验证 P1-8 修复 — Range 切换时 IsRange7d/24h/30d 反映 VM
    [Fact]
    public void SelectedRangeChangeUpdatesRangeFlags()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"X",
             "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":50,"reset_at":null,"reset_text":""},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":80,"reset_at":null,"reset_text":""}]}
            """);
        temp.WriteFile(".cache/history.jsonl", "");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);
        vm.Load();

        Assert.True(vm.IsRange7d);
        Assert.False(vm.IsRange24h);
        Assert.False(vm.IsRange30d);

        vm.SelectedRange = TrendRange.Hours24;
        Assert.True(vm.IsRange24h);
        Assert.False(vm.IsRange7d);
        Assert.False(vm.IsRange30d);

        vm.SelectedRange = TrendRange.Days30;
        Assert.True(vm.IsRange30d);
        Assert.False(vm.IsRange24h);
        Assert.False(vm.IsRange7d);
    }

    // P2-16 修复:验证 N3 修复 — Load() 在 cache 干净时清空 ErrorMessage
    // (避免旧错误横幅永久残留);cache 含 error 时保留/设置 ErrorMessage
    [Fact]
    public void LoadClearsErrorMessageWhenCacheIsClean()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"X",
             "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":50,"reset_at":null,"reset_text":""},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":80,"reset_at":null,"reset_text":""}]}
            """);
        temp.WriteFile(".cache/history.jsonl", "");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);

        // 模拟之前有错误(例如手动刷新失败过)
        vm.ErrorMessage = "API 调用失败（已重试 3 次）: timeout";
        vm.Load();

        // N3 修复后:cache 干净时,Load 应清空 ErrorMessage
        Assert.Equal("", vm.ErrorMessage);
    }

    [Fact]
    public void LoadKeepsErrorMessageWhenCacheHasError()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"error","plan":"X",
             "last_update":"2026-07-22T12:00:00+08:00","error":"API Key 无效","items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":50,"reset_at":null,"reset_text":""},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":80,"reset_at":null,"reset_text":""}]}
            """);
        temp.WriteFile(".cache/history.jsonl", "");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);

        vm.Load();

        // cache 自己说 error,Load 应保留 ErrorMessage
        Assert.Equal("API Key 无效", vm.ErrorMessage);
    }

    // S1 配套修复回归:history.jsonl 变化后第二次 Load 必须重新读取
    // (追加一个间隔 >45 分钟的点使分段数增加,证明新数据确实进入了趋势)
    [Fact]
    public void ReloadsHistoryAfterFileGrows()
    {
        using var temp = new TempDirectory();
        temp.WriteFile(".cache/cache.json", """
            {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"X",
             "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[
               {"model_id":"general","window_id":"5h","name":"5h","remaining_pct":50,"reset_at":null,"reset_text":""},
               {"model_id":"general","window_id":"weekly","name":"weekly","remaining_pct":80,"reset_at":null,"reset_text":""}]}
            """);
        var historyPath = System.IO.Path.Combine(temp.Path, ".cache", "history.jsonl");
        var line1 = """{"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-22T06:00:00+08:00","windows":[{"model_id":"general","window_id":"5h","remaining_pct":50},{"model_id":"general","window_id":"weekly","remaining_pct":90}]}""";
        var line2 = """{"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-22T06:30:00+08:00","windows":[{"model_id":"general","window_id":"5h","remaining_pct":60},{"model_id":"general","window_id":"weekly","remaining_pct":90}]}""";
        var line3 = """{"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-22T08:00:00+08:00","windows":[{"model_id":"general","window_id":"5h","remaining_pct":70},{"model_id":"general","window_id":"weekly","remaining_pct":90}]}""";
        System.IO.File.WriteAllText(historyPath, line1 + "\n" + line2 + "\n");

        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var vm = new MainViewModel(temp.Path, new UsageCacheReader(), new HistoryReader(), () => now);
        vm.Load();
        Assert.Single(vm.FiveHourSegments); // 06:00 与 06:30 间隔 30 分钟 < 45 分钟,同段

        System.IO.File.AppendAllText(historyPath, line3 + "\n");
        vm.Load();
        Assert.Equal(2, vm.FiveHourSegments.Count); // 08:00 与 06:30 间隔 90 分钟,新段
    }
}