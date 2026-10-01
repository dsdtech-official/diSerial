using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Choose data export options.
public partial class ExportDialog : Window
{
    // Initialize the export dialog.
    public ExportDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<ExportDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
