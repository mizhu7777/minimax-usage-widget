using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Converters;

/// <summary>
/// P0-1 修复:圆环强调色按设计规范 §7.1 独立映射 ——
/// Normal 时 5 小时环蓝(#2583F7)、周环紫(#8B5CF6);Warning 橙、Critical 红(两环同色)。
/// ConverterParameter 传 "weekly" 标识周环,其余视为 5 小时环。
/// 画刷为静态冻结单例(P2-15),不再每次 Convert 分配。
/// </summary>
public sealed class RingAccentConverter : IValueConverter
{
    private static readonly Brush FiveHourNormalBrush = CreateFrozen(0x25, 0x83, 0xF7);
    private static readonly Brush WeeklyNormalBrush = CreateFrozen(0x8B, 0x5C, 0xF6);
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
        var weekly = string.Equals(parameter as string, "weekly", StringComparison.OrdinalIgnoreCase);
        if (value is not QuotaSeverity severity)
            return weekly ? WeeklyNormalBrush : FiveHourNormalBrush;

        return severity switch
        {
            QuotaSeverity.Warning => WarningBrush,
            QuotaSeverity.Critical => CriticalBrush,
            _ => weekly ? WeeklyNormalBrush : FiveHourNormalBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
