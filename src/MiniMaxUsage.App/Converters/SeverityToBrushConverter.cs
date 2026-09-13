using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Converters;

/// <summary>P1-9 修复:QuotaSeverity → 状态文字刷色(Normal 绿,Warning 橙,Critical 红)。</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    // P2-15 修复:静态冻结单例,替代每次 Convert 分配新画刷
    private static readonly Brush NormalBrush = CreateFrozen(34, 197, 94);
    private static readonly Brush WarningBrush = CreateFrozen(245, 158, 11);
    private static readonly Brush CriticalBrush = CreateFrozen(239, 68, 68);

    private static Brush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not QuotaSeverity sev) return Brushes.Black;
        return sev switch
        {
            QuotaSeverity.Normal   => NormalBrush,
            QuotaSeverity.Warning  => WarningBrush,
            QuotaSeverity.Critical => CriticalBrush,
            _ => Brushes.Black,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
