using MiniMaxUsage.App.Services;

namespace MiniMaxUsage.App.Tests;

public sealed class ProjectLocatorTests
{
    [Fact]
    public void FindsProjectRootViaExplicitArgument()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("scripts/Update-MinimaxUsage.ps1", "# fixture");

        var result = Services.ProjectLocator.Resolve(["--project-dir", temp.Path], temp.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal(temp.Path, result.ProjectDirectory);
    }

    [Fact]
    public void ReturnsErrorForMissingDirectory()
    {
        using var temp = new TempDirectory();

        var result = Services.ProjectLocator.Resolve([], temp.Path);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    // P4 修复回归:dotnet run 输出目录 bin\Debug\net8.0-windows\win-x64 距项目根 7 次校验,
    // 回溯上限必须 ≥ 8,否则 AGENTS.md 的开发态冒烟加载直接报"未找到项目目录"
    [Fact]
    public void FindsProjectRootByWalkingUpFromDeepOutputDirectory()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("scripts/Update-MinimaxUsage.ps1", "# fixture");
        var deep = System.IO.Path.Combine(temp.Path,
            "src", "MiniMaxUsage.App", "bin", "Debug", "net8.0-windows", "win-x64");
        System.IO.Directory.CreateDirectory(deep);

        var result = Services.ProjectLocator.Resolve([], deep);

        Assert.True(result.IsSuccess);
        Assert.Equal(temp.Path, result.ProjectDirectory);
    }
}