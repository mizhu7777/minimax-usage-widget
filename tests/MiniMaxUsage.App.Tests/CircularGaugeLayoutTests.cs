namespace MiniMaxUsage.App.Tests;

public sealed class CircularGaugeLayoutTests
{
    [Fact]
    public void TextXIsCenteredWhenControlIsSquare()
    {
        var (x, _) = Controls.CircularGauge.CalculateTextPosition(110, 110, 45, 22);
        Assert.Equal(32.5, x);
    }

    [Fact]
    public void TextXIsCenteredWhenControlIsWiderThanTall()
    {
        var (x, _) = Controls.CircularGauge.CalculateTextPosition(220, 110, 45, 22);
        Assert.Equal(87.5, x);
    }
}