using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CredBench.Windows.Converters;

public class TechnologyToColorConverter : IValueConverter
{
    private static SolidColorBrush? _successBrush;
    private static SolidColorBrush? _inactiveBrush;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        _successBrush ??= Application.Current.TryFindResource("Brush.Success") as SolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
        _inactiveBrush ??= Application.Current.TryFindResource("TextFillColorSecondaryBrush") as SolidColorBrush
            ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        return value is bool isDetected && isDetected ? _successBrush : _inactiveBrush!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
