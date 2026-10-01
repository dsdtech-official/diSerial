using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Request user confirmation.
public partial class ConfirmationDialog : Window
{
    // Initialize confirmation UI.
    public ConfirmationDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<ConfirmationDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
