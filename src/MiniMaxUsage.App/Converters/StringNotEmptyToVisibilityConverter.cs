using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MiniMaxUsage.App.Converters;

/// <summary>P1-1 修复:字符串非空 → Visible,空串或 null → Collapsed。</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
