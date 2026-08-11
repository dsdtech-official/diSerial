using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<ExportDialogViewModel>(this, vm =>
        {
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
