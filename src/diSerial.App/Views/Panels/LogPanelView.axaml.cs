using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiSerial.App.ViewModels.Panels;

namespace DiSerial.App.Views.Panels;

// Render the frame timeline.
public partial class LogPanelView : UserControl
{
    internal const double FixedColumnsWidth = 110 + 60 + 130 + 10;

    private const int SampleLength = 100;

    private double _lastWidth = -1;

    // Initialize the frame log view.
    public LogPanelView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    // Handle a frame row double tap.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: FrameViewModel frame }) return;
        if (DataContext is not LogPanelViewModel vm) return;

        vm.ShowFrameDataCommand.Execute(frame);
    }

    // Update the frame text width.
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - _lastWidth) < 0.5) return;
        _lastWidth = e.NewSize.Width;

        if (DataContext is not LogPanelViewModel vm) return;

        vm.SetDataColumnCharacters(FittingCharacters(e.NewSize.Width));
    }

    // Compute visible text capacity.
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

        if (sample.Width <= 0) return 0;

        return (int)(available / (sample.Width / SampleLength));
    }
}
