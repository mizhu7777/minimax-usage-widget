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
}