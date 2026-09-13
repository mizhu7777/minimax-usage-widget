using System.Globalization;
using System.Windows.Media;
using MiniMaxUsage.App.Converters;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Tests;

// P0-1 修复回归:圆环强调色按设计规范 §7.1 独立映射 —— Normal 态 5h 蓝/周紫,Warning 橙,Critical 红
public sealed class RingAccentConverterTests
{
    [Theory]
    [InlineData(QuotaSeverity.Normal, "5h", 0x25, 0x83, 0xF7)]
    [InlineData(QuotaSeverity.Normal, "weekly", 0x8B, 0x5C, 0xF6)]
    [InlineData(QuotaSeverity.Warning, "5h", 245, 158, 11)]
    [InlineData(QuotaSeverity.Warning, "weekly", 245, 158, 11)]
    [InlineData(QuotaSeverity.Critical, "5h", 239, 68, 68)]
    [InlineData(QuotaSeverity.Critical, "weekly", 239, 68, 68)]
    public void MapsSeverityAndRingToSpecColors(QuotaSeverity severity, string ring, byte r, byte g, byte b)
    {
        var brush = Assert.IsType<SolidColorBrush>(
            new RingAccentConverter().Convert(severity, typeof(Brush), ring, CultureInfo.InvariantCulture));

        Assert.Equal(Color.FromRgb(r, g, b), brush.Color);
    }

    // P2-15 修复回归:转换器画刷必须是冻结单例,不产生未冻结分配
    [Fact]
    public void ReturnedBrushesAreFrozen()
    {
        var converter = new RingAccentConverter();
        foreach (var severity in new[] { QuotaSeverity.Normal, QuotaSeverity.Warning, QuotaSeverity.Critical })
        {
            var brush = (SolidColorBrush)converter.Convert(severity, typeof(Brush), "5h", CultureInfo.InvariantCulture);
            Assert.True(brush.IsFrozen);
        }
    }
}
