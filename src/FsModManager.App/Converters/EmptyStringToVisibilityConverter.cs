using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FsModManager.App.Converters;

/// <summary>Converts a null/empty string to Collapsed and any non-empty string to Visible — used for the search box's clear button.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
