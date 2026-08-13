namespace MiniMaxUsage.App.Tests;

public sealed class CircularGaugeTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(54, 145.8)]
    [InlineData(101, 270)]
    public void GaugeSweepClampsToZeroAndHundred(double value, double expected)
    {
        Assert.Equal(expected, Controls.CircularGauge.CalculateSweep(value), 3);
    }
}