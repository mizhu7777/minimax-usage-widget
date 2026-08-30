using System.Globalization;
using System.Windows.Data;

namespace MiniMaxUsage.App.Converters;

/// <summary>P1-9 修复:bool 取反(IsRefreshing=true → IsEnabled=false)。</summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
