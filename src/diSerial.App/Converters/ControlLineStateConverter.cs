using System.Globalization;
using Avalonia.Collections;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DiSerial.Core.Models;

namespace DiSerial.App.Converters;

/// <summary>
/// Turns a <see cref="ControlLineState"/> into one visual aspect of its indicator dot
/// (T-07, spec 4.15).
///
/// <para>⛔ <b>Three aspects, because promise 5 forbids conveying the state by colour alone.</b>
/// A green/grey pair is invisible to a colour-blind reader, and the state that matters most is
/// the third one: <see cref="ControlLineState.Unknown"/> means "nobody looked", which is a
/// different claim from "the line is low". So the dot changes <b>fill, outline and outline
/// style</b> together, and the view puts a translated word next to it as well.</para>
///
/// <list type="table">
///   <item><term>High</term><description>solid green, no outline</description></item>
///   <item><term>Low</term><description>solid grey, no outline</description></item>
///   <item><term>Unknown</term><description>hollow, dashed grey outline</description></item>
/// </list>
///
/// <para>⚠️ <b>One converter with a parameter rather than three classes</b>: the three aspects
/// are one decision expressed three ways, and splitting them is how two of them end up
/// disagreeing about what "unknown" looks like.</para>
///
/// <para>The parameter is <c>Fill</c>, <c>Stroke</c> or <c>Dash</c>. An unrecognised parameter
/// returns <see cref="Avalonia.Data.BindingOperations.DoNothing"/> rather than a plausible
/// default — a typo in XAML should leave the visual obviously unset, not silently correct.</para>
/// </summary>
public sealed class ControlLineStateConverter : IValueConverter
{
    /// <summary>
    /// Asserted. ⚠️ <b>Not the theme accent</b>: the accent colour is used for selection and
    /// focus all over this app, and a dot that matches it reads as "selected" rather than "on".
    /// </summary>
    private static readonly IBrush HighBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0x4F));

    /// <summary>Not asserted. Deliberately semi-transparent so it recedes beside the green.</summary>
    private static readonly IBrush LowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));

    /// <summary>The outline that only <see cref="ControlLineState.Unknown"/> draws.</summary>
    private static readonly IBrush UnknownStroke = new SolidColorBrush(Color.FromArgb(0x90, 0x80, 0x80, 0x80));

    private static readonly AvaloniaList<double> DashPattern = [2, 2];

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

            // Only the unknown dot is outlined. Outlining all three would make the hollow one
            // read as "a dot with a ring" instead of "a dot that is not filled in".
            "Stroke" => state is ControlLineState.Unknown ? UnknownStroke : Brushes.Transparent,

            "Dash" => state is ControlLineState.Unknown ? DashPattern : null,

            _ => Avalonia.Data.BindingOperations.DoNothing
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
