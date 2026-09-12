using System.Diagnostics;
using System.IO;

namespace MiniMaxUsage.App.Services;

public sealed record RefreshResult(bool Success, string? Error);

public static class PowerShellRefreshService
{
    // P1-3 修复:50s 太紧,API 重试 3×10s + 2×5s sleep = 40s,加 PS 启动 + 文件 IO
    // 实际最坏 55-60s 会撞上;放宽到 90s 留出足够缓冲
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    // P3-2 修复:用全路径到 powershell.exe 而不是 PATH 上的 "powershell.exe",
    // 避免 PATH 劫持;WinPS 5.1 在 System32 下固定存在
    private static readonly string PowerShellExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    public static ProcessStartInfo CreateStartInfo(string projectRoot)
    {
        var updater = Path.Combine(projectRoot, "scripts", "Update-MinimaxUsage.ps1");
        var info = new ProcessStartInfo(PowerShellExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // P1-2 修复:不重定向 stdout/stderr,避免子进程输出超过管道缓冲
            // 阻塞在 Write 上导致 50s/90s 超时误报;PowerShell 启动参数让
            // 任何意外输出写到 PowerShell.exe 自己的 stderr 窗口(不可见)
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(updater);
        return info;
    }

    public static Task<RefreshResult> RunAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        return RunAsync(projectRoot, DefaultTimeout, cancellationToken);
    }

    public static async Task<RefreshResult> RunAsync(string projectRoot, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = CreateStartInfo(projectRoot);
            using var process = new Process { StartInfo = info };
            process.Start();

            // O6 修复:WaitForExit 不响应取消令牌 —— 注册回调,取消时杀掉整棵进程树,
            // 避免取消后子进程(可能还持有 Global 互斥体)泄漏
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            });

            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new RefreshResult(false, $"刷新脚本超时(>{timeout.TotalSeconds:F0}s)。");
            }

            if (process.ExitCode != 0)
                return new RefreshResult(false, $"刷新脚本退出码: {process.ExitCode}");

            return new RefreshResult(true, null);
        }
        catch (Exception ex)
        {
            return new RefreshResult(false, $"刷新失败: {ex.Message}");
        }
    }
}