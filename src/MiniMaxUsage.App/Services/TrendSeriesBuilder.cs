using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Services;

public static class TrendSeriesBuilder
{
    private static readonly TimeSpan GapThreshold = TimeSpan.FromMinutes(45);

    public static IReadOnlyList<TrendSegment> Build(
        IReadOnlyList<HistorySample> samples,
        TrendRange range,
        DateTimeOffset now,
        Func<HistorySample, double> valueSelector)
    {
        if (samples.Count == 0)
            return [];

        var duration = ToDuration(range);
        var cutoff = now - duration;
        var filtered = samples.Where(s => s.RecordedAt >= cutoff).ToList();

        if (filtered.Count == 0)
            return [];

        var segments = new List<TrendSegment>();
        var currentPoints = new List<TrendPoint>
        {
            new(filtered[0].RecordedAt, Math.Clamp(valueSelector(filtered[0]), 0, 100))
        };

        for (int i = 1; i < filtered.Count; i++)
        {
            var prev = filtered[i - 1];
            var curr = filtered[i];

            if (curr.RecordedAt - prev.RecordedAt > GapThreshold)
            {
                segments.Add(new TrendSegment(currentPoints));
                currentPoints = [];
            }

            currentPoints.Add(new TrendPoint(curr.RecordedAt, Math.Clamp(valueSelector(curr), 0, 100)));
        }

        if (currentPoints.Count > 0)
            segments.Add(new TrendSegment(currentPoints));

        return segments;
    }

    private static TimeSpan ToDuration(TrendRange range) => range switch
    {
        TrendRange.Hours24 => TimeSpan.FromHours(24),
        TrendRange.Days7 => TimeSpan.FromDays(7),
        TrendRange.Days30 => TimeSpan.FromDays(30),
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };
}