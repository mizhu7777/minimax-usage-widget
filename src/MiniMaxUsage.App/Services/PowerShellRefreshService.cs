using System.Diagnostics;
using System.IO;

namespace MiniMaxUsage.App.Services;

public sealed record RefreshResult(bool Success, string? Error);

public static class PowerShellRefreshService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(50);

    public static ProcessStartInfo CreateStartInfo(string projectRoot)
    {
        var updater = Path.Combine(projectRoot, "scripts", "Update-MinimaxUsage.ps1");
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new RefreshResult(false, "刷新脚本超时。");
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