using System.IO;
using System.Text.Json;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Services;

public sealed class HistoryReader
{
    /// <summary>
    /// 读取历史快照;与采集器发生文件竞争等 IO 故障时返回 <c>null</c>(而非抛异常),
    /// 调用方应保留上次数据等待下个周期重试。正常路径永不为 null。
    /// </summary>
    public IReadOnlyList<HistorySample>? Read(string path, DateTimeOffset cutoff)
    {
        if (!File.Exists(path))
            return [];

        var samples = new List<HistorySample>();
        try
        {
            // P1-8 修复:显式 FileShare.ReadWrite|Delete —— 采集器会以写方式打开本文件追加,
            // 默认 FileShare.Read 的语义是"允许别人读、拒绝别人写",与已存在的写句柄冲突,
            // 内核将抛 ERROR_SHARING_VIOLATION;显式共享模式让读方永不与写方互斥
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // P1-8 修复:O8 之后未捕获异常会弹出模态「继续/退出」对话框,常驻监控不可接受;
            // 竞争发生时返回 null,调用方保留旧数据
            return null;
        }

        samples.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
        return samples;
    }
}
