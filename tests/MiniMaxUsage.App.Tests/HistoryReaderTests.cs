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

        Assert.Single(samples);
        Assert.Equal(80, samples[0].FiveHourPercent);
        Assert.Equal(95, samples[0].WeeklyPercent);
    }
}