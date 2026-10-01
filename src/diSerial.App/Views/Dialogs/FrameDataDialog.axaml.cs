using Avalonia.Controls;
using Avalonia.Threading;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Display complete frame data.
public partial class FrameDataDialog : Window
{
    // Initialize frame data controls.
    public FrameDataDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<FrameDataDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });

        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            DataBox.Focus();
            DataBox.SelectAll();
        }, DispatcherPriority.Input);
    }
}
