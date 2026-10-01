using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiSerial.App.ViewModels.Sessions;

namespace DiSerial.App.Views.Sessions;

// Display a terminal session.
public partial class TerminalSessionView : UserControl
{
    // Initialize the terminal view.
    public TerminalSessionView() => InitializeComponent();


    // Focus the terminal send field.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SendPanel.FocusInput();
    }

    // Resize the adjacent panels.
    private void OnPanelSplitterDrag(object? sender, VectorEventArgs e)
    {
        if (DataContext is TerminalSessionViewModel session)
        {
            session.PanelHeight -= e.Vector.Y;
        }
    }
}
