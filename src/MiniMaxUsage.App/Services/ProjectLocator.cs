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
        // --project-dir argument takes precedence
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--project-dir" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                var dir = Path.GetFullPath(args[i + 1]);
                return Validate(dir);
            }
        }

        // Walk up from baseDirectory looking for the scripts directory
        var current = Path.GetFullPath(baseDirectory);
        for (int i = 0; i < 4; i++)
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

    private static ProjectLocationResult Validate(string directory)
    {
        var updater = Path.Combine(directory, "scripts", "Update-MinimaxUsage.ps1");
        if (File.Exists(updater))
            return new ProjectLocationResult(directory, null);

        return new ProjectLocationResult(null, null);
    }
}