using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CredBench.Windows.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null;
        var invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) ?? false;

        if (invert)
            isNull = !isNull;

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
