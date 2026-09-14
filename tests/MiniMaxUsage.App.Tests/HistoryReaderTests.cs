using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.Services;

namespace MiniMaxUsage.App.Tests;

public sealed class HistoryReaderTests
{
    [Fact]
    public void ReadsValidGeneralSamplesAndSkipsMalformedLines()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("history.jsonl", """
            {bad json
            {"schema_version":1,"provider_id":"minimax","recorded_at":"2026-04-01T00:00:00+08:00","windows":[]}
            {"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-01T00:00:00+08:00","windows":[{"model_id":"general","window_id":"5h","remaining_pct":80},{"model_id":"general","window_id":"weekly","remaining_pct":95}]}
            {"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-02T00:00:00+08:00","windows":[{"model_id":"video","window_id":"5h","remaining_pct":50}]}
            """);

        var cutoff = DateTimeOffset.Parse("2026-06-01T00:00:00+08:00");
        var samples = new Services.HistoryReader().Read(path, cutoff);

        Assert.NotNull(samples);
        Assert.Single(samples);
        Assert.Equal(80, samples[0].FiveHourPercent);
        Assert.Equal(95, samples[0].WeeklyPercent);
    }

    // P1-8 修复回归:文件被独占锁定(与采集器竞争的极端场景)时返回 null 而不是抛异常,
    // 避免未捕获异常触发 O8 的模态「继续/退出」对话框打断常驻监控
    [Fact]
    public void ReturnsNullWhenFileIsExclusivelyLocked()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("history.jsonl", """
            {"schema_version":1,"provider_id":"minimax","recorded_at":"2026-07-01T00:00:00+08:00","windows":[{"model_id":"general","window_id":"5h","remaining_pct":80},{"model_id":"general","window_id":"weekly","remaining_pct":95}]}
            """);

        using var exclusive = new System.IO.FileStream(
            path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);

        var result = new Services.HistoryReader().Read(path, DateTimeOffset.Parse("2026-06-01T00:00:00+08:00"));

        Assert.Null(result);
    }
}