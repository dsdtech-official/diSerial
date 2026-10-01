using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Configure a new session.
public partial class NewSessionDialog : Window
{
    // Initialize new session controls.
    public NewSessionDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<NewSessionDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, bool confirmed) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
