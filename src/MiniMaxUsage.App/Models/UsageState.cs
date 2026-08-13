namespace MiniMaxUsage.App.Models;

public sealed record QuotaWindow(string WindowId, double RemainingPercent, DateTimeOffset? ResetAt, string ResetText);

public sealed record CurrentUsage(
    string ProviderId,
    string Plan,
    DateTimeOffset LastUpdate,
    QuotaWindow FiveHour,
    QuotaWindow Weekly,
    bool IsStale,
    string? Error);

public sealed record UsageReadResult(CurrentUsage? Value, string? FatalError)
{
    public bool IsSuccess => Value is not null;
    public static UsageReadResult Success(CurrentUsage value) => new(value, null);
    public static UsageReadResult Failure(string error) => new(null, error);
}

public enum QuotaSeverity { Normal, Warning, Critical }

public static class QuotaSeverityRules
{
    public static QuotaSeverity FromPercent(double percent) => percent switch
    {
        <= 10 => QuotaSeverity.Critical,
        <= 20 => QuotaSeverity.Warning,
        _ => QuotaSeverity.Normal
    };
}

public sealed record HistorySample(DateTimeOffset RecordedAt, double FiveHourPercent, double WeeklyPercent);
public sealed record TrendPoint(DateTimeOffset RecordedAt, double Value);
public sealed record TrendSegment(IReadOnlyList<TrendPoint> Points);
public enum TrendRange { Hours24, Days7, Days30 }