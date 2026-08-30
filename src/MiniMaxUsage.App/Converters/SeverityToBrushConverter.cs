using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MiniMaxUsage.App.Models;

namespace MiniMaxUsage.App.Converters;

/// <summary>P1-9 修复:QuotaSeverity → 刷色(Normal 绿,Warning 橙,Critical 红)。</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not QuotaSeverity sev) return Brushes.Black;
        return sev switch
        {
            QuotaSeverity.Normal   => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // 绿
            QuotaSeverity.Warning  => new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // 橙
            QuotaSeverity.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)),   // 红
            _ => Brushes.Black,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
