using System.Text.Json.Serialization;

namespace MiniMaxUsage.App.Models;

public sealed class UsageCacheDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("provider_id")] public string ProviderId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("plan")] public string Plan { get; init; } = "";
    [JsonPropertyName("last_update")] public DateTimeOffset? LastUpdate { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("items")] public List<UsageWindowDocument> Items { get; init; } = [];
}

public sealed class UsageWindowDocument
{
    [JsonPropertyName("model_id")] public string ModelId { get; init; } = "";
    [JsonPropertyName("window_id")] public string WindowId { get; init; } = "";
    [JsonPropertyName("remaining_pct")] public double RemainingPercent { get; init; }
    [JsonPropertyName("reset_at")] public DateTimeOffset? ResetAt { get; init; }
    [JsonPropertyName("reset_text")] public string ResetText { get; init; } = "";
}

public sealed class HistorySnapshotDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("provider_id")] public string ProviderId { get; init; } = "";
    [JsonPropertyName("recorded_at")] public DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("windows")] public List<HistoryWindowDocument> Windows { get; init; } = [];
}

public sealed class HistoryWindowDocument
{
    [JsonPropertyName("model_id")] public string ModelId { get; init; } = "";
    [JsonPropertyName("window_id")] public string WindowId { get; init; } = "";
    [JsonPropertyName("remaining_pct")] public double RemainingPercent { get; init; }
}