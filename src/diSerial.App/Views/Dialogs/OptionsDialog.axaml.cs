using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Edit application options.
public partial class OptionsDialog : Window
{
    // Initialize app option controls.
    public OptionsDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<OptionsDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
