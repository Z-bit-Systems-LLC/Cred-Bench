using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CredBench.Windows.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Handle both boolean values and null checks for objects
        var boolValue = value switch
        {
            bool b => b,
            null => false,
            _ => true // Any non-null object is treated as true
        };

        var invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) ?? false;

        if (invert)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
