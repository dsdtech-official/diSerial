using System.Globalization;
using Avalonia.Collections;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DiSerial.Core.Models;

namespace DiSerial.App.Converters;

// Render control line indicators.
public sealed class ControlLineStateConverter : IValueConverter
{
    private static readonly IBrush HighBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0x4F));

    private static readonly IBrush LowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));

    private static readonly IBrush UnknownStroke = new SolidColorBrush(Color.FromArgb(0x90, 0x80, 0x80, 0x80));

    private static readonly AvaloniaList<double> DashPattern = [2, 2];

    // Style a control line indicator.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value as ControlLineState? ?? ControlLineState.Unknown;
        var aspect = parameter as string;

        return aspect switch
        {
            "Fill" => state switch
            {
                ControlLineState.High => HighBrush,
                ControlLineState.Low => LowBrush,
                _ => Brushes.Transparent
            },

            "Stroke" => state is ControlLineState.Unknown ? UnknownStroke : Brushes.Transparent,

            "Dash" => state is ControlLineState.Unknown ? DashPattern : null,

            _ => Avalonia.Data.BindingOperations.DoNothing
        };
    }

    // Reject reverse conversion.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
