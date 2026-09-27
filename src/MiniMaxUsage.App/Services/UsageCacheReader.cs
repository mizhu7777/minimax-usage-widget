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
            // G13 修复:显式 FileShare.ReadWrite|Delete,与 HistoryReader 一致 ——
            // File.ReadAllText 默认 FileShare.Read 会拒绝采集器的原子改名
            // (Move-Item -Force 需要目标句柄允许删除),竞争时把一次正常刷新误判为 API 失败
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false));
            var json = reader.ReadToEnd();
            document = JsonSerializer.Deserialize<UsageCacheDocument>(json) ?? throw new JsonException("null document");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NullReferenceException)
        {
            return UsageReadResult.Failure($"缓存读取失败: {ex.Message}");
        }

        if (document.SchemaVersion < 2 || document.ProviderId != "minimax")
            return UsageReadResult.Failure("缓存格式不兼容 (schema_version < 2 或 provider 不匹配)");

        // C7 修复:cache.json 可能含 "items": null(Items 反序列化为 null,
        // FirstOrDefault 对 null 源抛 ArgumentNullException)或 "items": [null]
        // (lambda 内抛 NullReferenceException)—— 原实现两者都逃出上方 catch 过滤器,
        // 直冲全局异常弹窗。在此归一化为空数组并剔除 null 元素
        var items = (document.Items ?? []).Where(x => x is not null).ToList();

        var five = items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "5h");
        var weekly = items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "weekly");
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
