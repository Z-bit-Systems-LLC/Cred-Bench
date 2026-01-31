using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CredBench.Windows.Converters;

public class TechnologyToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isDetected)
            return Brushes.Gray;

        return isDetected
            ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) // Green
            : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)); // Gray
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
