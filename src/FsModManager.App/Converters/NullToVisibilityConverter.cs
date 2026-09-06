using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FsModManager.App.Converters;

/// <summary>
/// Converts null (or missing) to Collapsed and non-null to Visible — used to swap between a
/// decoded image and a placeholder glyph. Pass ConverterParameter="Invert" for the opposite mapping.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isNull = !isNull;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
