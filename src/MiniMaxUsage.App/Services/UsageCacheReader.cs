using System.IO;
using System.Text.Json;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Services;

public sealed class UsageCacheReader
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(3);

    public UsageReadResult Read(string path, DateTimeOffset now)
    {
        UsageCacheDocument document;
        try
        {
            var json = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
            document = JsonSerializer.Deserialize<UsageCacheDocument>(json) ?? throw new JsonException("null document");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return UsageReadResult.Failure($"缓存读取失败: {ex.Message}");
        }

        if (document.SchemaVersion < 2 || document.ProviderId != "minimax")
            return UsageReadResult.Failure("缓存格式不兼容 (schema_version < 2 或 provider 不匹配)");

        var five = document.Items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "5h");
        var weekly = document.Items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "weekly");
        if (five is null || weekly is null || document.LastUpdate is null)
        {
            // P2 修复:首次失败(无旧数据可保留)时优先返回缓存里的真实错误,
            // 否则"API Key 无效"等根因被"缺少窗口"覆盖,误导排障
            if (document.Status == "error" && !string.IsNullOrEmpty(document.Error))
                return UsageReadResult.Failure(document.Error);
            return UsageReadResult.Failure("缓存缺少 general 模型的 5h/weekly 窗口或更新时间。");
        }

        QuotaWindow Convert(UsageWindowDocument item) => new(
            item.WindowId,
            Math.Clamp(item.RemainingPercent, 0, 100),
            item.ResetAt,
            item.ResetText);

        return UsageReadResult.Success(new CurrentUsage(
            document.ProviderId,
            document.Plan,
            document.LastUpdate.Value,
            Convert(five),
            Convert(weekly),
            now - document.LastUpdate.Value > StaleThreshold,
            document.Status == "ok" ? null : document.Error ?? "最近一次采集失败。"));
    }
}