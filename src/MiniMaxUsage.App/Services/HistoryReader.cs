using System.IO;
using System.Text.Json;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Services;

public sealed class HistoryReader
{
    public IReadOnlyList<HistorySample> Read(string path, DateTimeOffset cutoff)
    {
        if (!File.Exists(path))
            return [];

        var samples = new List<HistorySample>();

        foreach (var line in File.ReadLines(path))
        {
            HistorySnapshotDocument? doc;
            try
            {
                doc = JsonSerializer.Deserialize<HistorySnapshotDocument>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (doc is null || doc.SchemaVersion < 1 || doc.ProviderId != "minimax")
                continue;

            if (doc.RecordedAt < cutoff)
                continue;

            var fiveHour = doc.Windows.FirstOrDefault(w => w.ModelId == "general" && w.WindowId == "5h");
            var weekly = doc.Windows.FirstOrDefault(w => w.ModelId == "general" && w.WindowId == "weekly");
            if (fiveHour is null || weekly is null)
                continue;

            samples.Add(new HistorySample(
                doc.RecordedAt,
                Math.Clamp(fiveHour.RemainingPercent, 0, 100),
                Math.Clamp(weekly.RemainingPercent, 0, 100)));
        }

        samples.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
        return samples;
    }
}