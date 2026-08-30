namespace MiniMaxUsage.App.Tests;

public sealed class PowerShellRefreshServiceTests
{
    [Fact]
    public void CreatesHiddenPowerShellCommandForExistingUpdater()
    {
        using var temp = new TempDirectory();
        var root = temp.Path;
        temp.WriteFile("scripts/Update-MinimaxUsage.ps1", "# fixture");
        var info = Services.PowerShellRefreshService.CreateStartInfo(root);

        // P3-2 修复后:FileName 现在是全路径到 System32 下的 powershell.exe
        // 不再断言 "powershell.exe" 字符串相等,改为断言以 \WindowsPowerShell\v1.0\powershell.exe 结尾
        Assert.EndsWith(
            System.IO.Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
            info.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        // P1-2 修复后:不再重定向 stdout/stderr
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Contains("-NoProfile", info.ArgumentList);
        Assert.Contains(System.IO.Path.Combine(root, "scripts", "Update-MinimaxUsage.ps1"), info.ArgumentList);
    }

    [Fact]
    public void DefaultTimeoutIsAtLeast90Seconds()
    {
        // P1-3 修复后:DefaultTimeout 从 50s 提到 90s,留出 API 重试 (3×10s+2×5s) 的缓冲
        // 不能直接访问 private 字段,但可以间接验证:通过反射或运行一个长任务
        // 这里用反射简单读一下
        var field = typeof(Services.PowerShellRefreshService).GetField(
            "DefaultTimeout",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var value = (TimeSpan)field!.GetValue(null)!;
        Assert.True(value.TotalSeconds >= 90, $"DefaultTimeout = {value.TotalSeconds}s, 期望 ≥ 90s");
    }
}