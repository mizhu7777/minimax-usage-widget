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

        Assert.Equal("powershell.exe", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Contains("-NoProfile", info.ArgumentList);
        Assert.Contains(System.IO.Path.Combine(root, "scripts", "Update-MinimaxUsage.ps1"), info.ArgumentList);
    }
}