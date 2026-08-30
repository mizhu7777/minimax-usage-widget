using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Tests;

// P2-15 修复:从 MainViewModelTests 移出,放到自己的文件
public sealed class QuotaSeverityRulesTests
{
    [Theory]
    [InlineData(0, QuotaSeverity.Critical)]
    [InlineData(10, QuotaSeverity.Critical)]
    [InlineData(11, QuotaSeverity.Warning)]
    [InlineData(20, QuotaSeverity.Warning)]
    [InlineData(21, QuotaSeverity.Normal)]
    [InlineData(100, QuotaSeverity.Normal)]
    public void FromPercentReturnsExpectedSeverity(double percent, QuotaSeverity expected)
    {
        Assert.Equal(expected, QuotaSeverityRules.FromPercent(percent));
    }
}
