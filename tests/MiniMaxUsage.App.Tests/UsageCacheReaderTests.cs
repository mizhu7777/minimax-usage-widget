using MiniMaxUsage.App.Services;

namespace MiniMaxUsage.App.Tests;

public sealed class UsageCacheReaderTests
{
    [Fact]
    public void ReadsGeneralWindowsAndMarksStaleAfterThreeMinutes()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:05:01+08:00");
        var json = """
        {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"MiniMax TokenPlan",
         "last_update":"2026-07-22T12:02:00+08:00","error":null,"items":[
           {"model_id":"video","window_id":"5h","name":"视频","remaining_pct":12,"reset_at":null,"reset_text":"N/A"},
           {"model_id":"general","window_id":"weekly","name":"文本周","remaining_pct":87,"reset_at":"2026-07-26T16:00:00Z","reset_text":"4天后"},
           {"model_id":"general","window_id":"5h","name":"文本5h","remaining_pct":54,"reset_at":"2026-07-22T16:00:00Z","reset_text":"2小时后"}]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(54, result.Value!.FiveHour.RemainingPercent);
        Assert.Equal(87, result.Value.Weekly.RemainingPercent);
        Assert.True(result.Value.IsStale);
    }

    [Fact]
    public void ErrorStatusPreservesValuesAndSetsError()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var json = """
        {"schema_version":2,"provider_id":"minimax","status":"error","plan":"MiniMax TokenPlan",
         "last_update":"2026-07-22T12:00:00+08:00","error":"API Key 无效","items":[
           {"model_id":"general","window_id":"5h","name":"文本5h","remaining_pct":54,"reset_at":"2026-07-22T16:00:00Z","reset_text":"2小时后"},
           {"model_id":"general","window_id":"weekly","name":"文本周","remaining_pct":87,"reset_at":"2026-07-26T16:00:00Z","reset_text":"4天后"}]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, now);

        Assert.True(result.IsSuccess);
        Assert.Equal("API Key 无效", result.Value!.Error);
        Assert.False(result.Value.IsStale);
    }

    [Fact]
    public void MalformedJsonReturnsFatalError()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", "{not valid json");

        var result = new Services.UsageCacheReader().Read(path, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void MissingGeneralModelReturnsFatalError()
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
        var json = """
        {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"Plan",
         "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[
           {"model_id":"video","window_id":"5h","name":"视频","remaining_pct":12,"reset_at":null,"reset_text":"N/A"}]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, now);

        Assert.False(result.IsSuccess);
    }

    // P2-16 修复:覆盖 schema_version < 2 / provider 错误分支
    [Fact]
    public void SchemaVersionBelow2ReturnsFatalError()
    {
        var json = """
        {"schema_version":1,"provider_id":"minimax","status":"ok","plan":"MiniMax",
         "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, DateTimeOffset.Now);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void WrongProviderReturnsFatalError()
    {
        var json = """
        {"schema_version":2,"provider_id":"openai","status":"ok","plan":"X",
         "last_update":"2026-07-22T12:00:00+08:00","error":null,"items":[]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, DateTimeOffset.Now);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.FatalError);
    }

    // P2 修复回归:首次失败(无旧数据可保留)时,应返回缓存里的真实错误而不是"缺少窗口"
    [Fact]
    public void ErrorStatusWithMissingWindowsReturnsRealError()
    {
        var json = """
        {"schema_version":2,"provider_id":"minimax","status":"error","plan":"MiniMax TokenPlan",
         "last_update":null,"error":"API Key 无效（status_code=2049）","items":[]}
        """;
        using var temp = new TempDirectory();
        var path = temp.WriteFile("cache.json", json);

        var result = new Services.UsageCacheReader().Read(path, DateTimeOffset.Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("API Key 无效（status_code=2049）", result.FatalError);
    }
}