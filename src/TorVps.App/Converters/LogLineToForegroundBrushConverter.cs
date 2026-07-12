using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TorVps.Core.Models;
using TorVps.Core.Services;

namespace TorVps.App.Converters;

/// <summary>Colors one runtime-log line by severity: the usual dim tone for normal lines, yellow for
/// warnings, red for errors (classification shared with Core's <see cref="LogLineClassifier"/>).</summary>
public sealed class LogLineToForegroundBrushConverter : IValueConverter
{
    private static readonly Brush Normal = Freeze(Color.FromRgb(0x9E, 0xB4, 0xD3)); // = TextDimBrush
    private static readonly Brush Warn = Freeze(Color.FromRgb(0xFF, 0xD6, 0x0A));   // = AccentYellowBrush
    private static readonly Brush Error = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));  // = AccentRedBrush

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string line
            ? LogLineClassifier.Classify(line) switch
            {
                LogLevel.Error => Error,
                LogLevel.Warn => Warn,
                _ => Normal,
            }
            : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
