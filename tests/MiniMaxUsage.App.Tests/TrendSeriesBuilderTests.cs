using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.Services;

namespace MiniMaxUsage.App.Tests;

public sealed class TrendSeriesBuilderTests
{
    [Fact]
    public void StartsNewSegmentAfterFortyFiveMinuteGap()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var samples = new[]
        {
            new HistorySample(now.AddMinutes(-70), 80, 95),
            new HistorySample(now.AddMinutes(-60), 78, 95),
            new HistorySample(now.AddMinutes(-10), 70, 94)
        };

        var series = Services.TrendSeriesBuilder.Build(samples, TrendRange.Hours24, now, x => x.FiveHourPercent);

        Assert.Equal(2, series.Count);
        Assert.Equal(2, series[0].Points.Count);
        Assert.Single(series[1].Points);
    }

    [Fact]
    public void ReturnsEmptyForNoData()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var series = Services.TrendSeriesBuilder.Build([], TrendRange.Days7, now, x => x.FiveHourPercent);

        Assert.Empty(series);
    }

    [Fact]
    public void FiltersByCutoff()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var samples = new[]
        {
            new HistorySample(now.AddDays(-8), 50, 60),
            new HistorySample(now.AddDays(-2), 55, 65)
        };

        var series = Services.TrendSeriesBuilder.Build(samples, TrendRange.Days7, now, x => x.WeeklyPercent);

        Assert.Single(series);
        Assert.Equal(65, series[0].Points[0].Value);
    }
}