using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DiSerial.App.ViewModels.Panels;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.Converters;

// Map channels to colors.
public sealed class ChannelToBrushConverter : IValueConverter
{
    private static readonly IBrush BrushA = SolidColorBrush.Parse(ChannelViewModel.ColorA);
    private static readonly IBrush BrushB = SolidColorBrush.Parse(ChannelViewModel.ColorB);
    private static readonly IBrush BrushNeutral = SolidColorBrush.Parse("#888780");

    // Select a channel color.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChannelId channel
            ? channel switch
            {
                ChannelId.A => BrushA,
                ChannelId.B => BrushB,
                _ => BrushNeutral
            }
            : BrushNeutral;

    // Reject reverse conversion.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Map frame accents to colors.
public sealed class FrameAccentToBrushConverter : IValueConverter
{
    public const string TxOnly = "TxOnly";

    public const string ColorTx = "#8C5BD8";

    private static readonly IBrush BrushA = SolidColorBrush.Parse(ChannelViewModel.ColorA);
    private static readonly IBrush BrushB = SolidColorBrush.Parse(ChannelViewModel.ColorB);
    private static readonly IBrush BrushTx = SolidColorBrush.Parse(ColorTx);

    private static object NotColoured => AvaloniaProperty.UnsetValue;

    // Select a frame accent color.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FrameAccent accent) return NotColoured;

        if (parameter as string == TxOnly)
        {
            return accent == FrameAccent.Tx ? BrushTx : NotColoured;
        }

        return accent switch
        {
            FrameAccent.ChannelA => BrushA,
            FrameAccent.ChannelB => BrushB,
            FrameAccent.Tx => BrushTx,
            _ => NotColoured
        };
    }

    // Reject reverse conversion.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Filter sent frame visibility.
public sealed class SentVisibilityConverter : IMultiValueConverter
{
    // Decide sent-frame visibility.
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTx = values.Count > 0 && values[0] is true;
        var showSent = values.Count < 2 || values[1] is not false;

        return !isTx || showSent;
    }
}

// Choose frame row backgrounds.
public sealed class FrameRowBackgroundConverter : IMultiValueConverter
{
    public const string ColorError = "#33E24B4A";

    public const string ColorTxBackground = "#2EF5C518";

    private static readonly IBrush ErrorBackground = SolidColorBrush.Parse(ColorError);
    private static readonly IBrush TxBackground = SolidColorBrush.Parse(ColorTxBackground);

    // Select a frame row background.
    public object Convert(
        IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is true) return ErrorBackground;
        if (values.Count > 1 && values[1] is FrameAccent.Tx) return TxBackground;

        return Brushes.Transparent;
    }
}

// Map state to border thickness.
public sealed class BoolToThicknessConverter : IValueConverter
{
    // Select the highlight thickness.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Thickness(2) : new Thickness(0);

    // Reject reverse conversion.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
