using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TorVps.Core.Models;

namespace TorVps.App.Converters;

/// <summary>Maps a HealthState to the tinted background brush used by status cards, health chips, and server chips.</summary>
public sealed class HealthStateToBackgroundBrushConverter : IValueConverter
{
    private static readonly Brush Ok = MakeGradient(Color.FromArgb(0x3D, 0x30, 0xD1, 0x58), Color.FromArgb(0x26, 0x30, 0xD1, 0x58));
    private static readonly Brush Warn = MakeGradient(Color.FromArgb(0x3D, 0xFF, 0xD6, 0x0A), Color.FromArgb(0x21, 0xFF, 0xD6, 0x0A));
    private static readonly Brush Error = MakeGradient(Color.FromArgb(0x40, 0xFF, 0x45, 0x3A), Color.FromArgb(0x21, 0xFF, 0x45, 0x3A));
    private static readonly Brush Unknown = MakeGradient(Color.FromArgb(0x38, 0x8E, 0x8E, 0x93), Color.FromArgb(0x1F, 0x8E, 0x8E, 0x93));

    private static Brush MakeGradient(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90.0);
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
