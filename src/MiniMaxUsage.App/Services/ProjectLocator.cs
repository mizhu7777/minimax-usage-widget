using System.IO;

namespace MiniMaxUsage.App.Services;

public sealed record ProjectLocationResult(string? ProjectDirectory, string? Error)
{
    public bool IsSuccess => ProjectDirectory is not null;
}

public static class ProjectLocator
{
    public static ProjectLocationResult Resolve(string[] args, string baseDirectory)
    {
        try
        {
            // --project-dir argument takes precedence
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--project-dir" && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    // 注:Path.GetFullPath 在收到非法字符/超长路径时会抛 ArgumentException/PathTooLongException,
                    // 该路径有外部 try/catch 兜底
                    var dir = Path.GetFullPath(args[i + 1]);
                    return Validate(dir);
                }
            }

            // Walk up from baseDirectory looking for the scripts directory
            // P4 修复:8 层 —— dotnet run 的输出在 bin\Debug\net8.0-windows\win-x64\(带 RID),
            // 距项目根 7 次校验,原来 4 层连 MiniMaxUsage.App 都没走到就停了
            var current = Path.GetFullPath(baseDirectory);
            for (int i = 0; i < 8; i++)
            {
                var result = Validate(current);
                if (result.IsSuccess)
                    return result;
                var parent = Path.GetDirectoryName(current);
                if (parent is null || parent == current)
                    break;
                current = parent;
            }

            return new ProjectLocationResult(null, "未找到项目目录。请使用 --project-dir 参数指定项目路径。");
        }
        catch (System.Exception ex)
        {
            // 路径非法/超长/权限不足等异常一律降级为"未找到",由上层弹框
            return new ProjectLocationResult(null, $"解析项目目录失败: {ex.Message}");
        }
    }

    private static ProjectLocationResult Validate(string directory)
    {
        // File.Exists 遇权限问题返回 false 不抛;真正的抛出点已在 Resolve 上层 try/catch
        var updater = Path.Combine(directory, "scripts", "Update-MinimaxUsage.ps1");
        if (File.Exists(updater))
            return new ProjectLocationResult(directory, null);

        return new ProjectLocationResult(null, null);
    }
}