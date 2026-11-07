using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OneCUpdate.Converters;

/// <summary>Строка вида "#RRGGBB" → кисть. Удобно для цветов статусов в моделях.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string hex = value as string ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
