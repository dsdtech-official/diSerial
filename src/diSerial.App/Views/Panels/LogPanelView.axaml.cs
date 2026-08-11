using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DiSerial.App.ViewModels.Panels;

namespace DiSerial.App.Views.Panels;

/// <summary>
/// P2-100 (2026-08-10): the only reason this view has code behind it.
///
/// <para><b>The defect.</b> In <c>HEX + ASCII</c> the line breaks were baked into the formatted
/// string every 16 bytes, so maximising the window widened the data column -- it is a <c>*</c>
/// column and always did widen -- while the text inside it did not change at all. The user saw a
/// large blank area to the right of the data.</para>
///
/// <para><b>Why measure here rather than compute in the view model.</b> How many characters fit
/// is a question about a <i>typeface at a size</i>, and the view is the only layer that knows
/// which one is in use. Hardcoding a pixels-per-character number in the view model would be a
/// second copy of the <c>TextBlock.mono</c> style, and it would go stale the day that style
/// changes -- silently, because the text would still render, just wrapped wrong.</para>
/// </summary>
public partial class LogPanelView : UserControl
{
    /// <summary>
    /// Total width of the fixed columns in <c>LogPanelView.axaml</c> (<c>110,60,200,*</c>) plus
    /// the data cell's left margin.
    ///
    /// <para>⛔ <b>This is a second copy of numbers that live in the markup</b>, and it is the
    /// weak point of this file. It is kept because the alternative -- reading the Grid's actual
    /// column widths -- means reaching into a generated item container, which only exists after
    /// the first row arrives and is null exactly when the empty display is being sized.</para>
    ///
    /// <para>⚠️ <b>The failure direction is mild and visible</b>: get it wrong and the text wraps
    /// slightly early or late, which is a layout nuisance, not wrong data. <c>ColumnWidthsAreInSync</c>
    /// in the test suite compares this against the markup so it cannot drift unnoticed.</para>
    /// </summary>
    internal const double FixedColumnsWidth = 110 + 60 + 130 + 10;

    /// <summary>A sample long enough that per-character rounding does not distort the average.</summary>
    private const int SampleLength = 100;

    private double _lastWidth = -1;

    public LogPanelView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // ⚠️ Width only. A height change cannot alter how many characters fit on a line, and
        // SizeChanged fires for both -- without this the whole display would re-format every
        // time a row arrived and the scroll extent grew.
        if (Math.Abs(e.NewSize.Width - _lastWidth) < 0.5) return;
        _lastWidth = e.NewSize.Width;

        if (DataContext is not LogPanelViewModel vm) return;

        vm.SetDataColumnCharacters(FittingCharacters(e.NewSize.Width));
    }

    /// <summary>
    /// How many monospace characters fit in the data column at the given control width.
    ///
    /// <para>⚠️ Measured with the same family and size as the <c>TextBlock.mono</c> style in
    /// <c>App.axaml</c>; <c>MonoStyleMatchesTheMeasurement</c> in the test suite fails if those
    /// two drift apart.</para>
    /// </summary>
    private static int FittingCharacters(double controlWidth)
    {
        var available = controlWidth - FixedColumnsWidth;
        if (available <= 0) return 0;

        var sample = new FormattedText(
            new string('0', SampleLength),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace")),
            12,
            Brushes.Black);

        // ⛔ A zero here would divide by zero. It is reachable: a headless or not-yet-realised
        // visual can measure to nothing, and this runs on the first SizeChanged.
        if (sample.Width <= 0) return 0;

        return (int)(available / (sample.Width / SampleLength));
    }
}
