using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TorVps.App.Converters;

/// <summary>Visible when the bound value is null, Collapsed otherwise. Used to hide a chip's plain text when a colored breakdown is present.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
