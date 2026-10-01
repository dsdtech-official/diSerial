using Avalonia.Controls;
using Avalonia.Input;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Manage recorded sessions.
public partial class RecordingManagerDialog : Window
{
    // Initialize recording controls.
    public RecordingManagerDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<RecordingManagerViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }

    // Select a batch on right-click.
    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (sender is not Control { DataContext: RecordingBatchItemViewModel row }) return;
        if (DataContext is not RecordingManagerViewModel vm) return;

        vm.SelectOnly(row);
    }
}
