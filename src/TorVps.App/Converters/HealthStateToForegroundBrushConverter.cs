using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TorVps.Core.Models;

namespace TorVps.App.Converters;

/// <summary>Maps a HealthState to its accent text color (green/yellow/red/gray).</summary>
public sealed class HealthStateToForegroundBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush Warn = Freeze(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush Error = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush Unknown = Freeze(Color.FromRgb(0xA1, 0xA1, 0xA8));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        HealthState.Ok => Ok,
        HealthState.Warn => Warn,
        HealthState.Error => Error,
        _ => Unknown,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
