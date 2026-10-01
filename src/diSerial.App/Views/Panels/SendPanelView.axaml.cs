using Avalonia.Controls;
using Avalonia.Threading;

namespace DiSerial.App.Views.Panels;

// Display payload send controls.
public partial class SendPanelView : UserControl
{
    // Initialize send controls.
    public SendPanelView() => InitializeComponent();

    // Focus the payload input field.
    public void FocusInput() =>
        Dispatcher.UIThread.Post(() => InputBox.Focus(), DispatcherPriority.Input);

    // Apply the selected command.
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is null) return;

        Dispatcher.UIThread.Post(() => combo.SelectedItem = null, DispatcherPriority.Background);
    }
}
