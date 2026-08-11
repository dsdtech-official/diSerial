using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

public partial class NewSessionDialog : Window
{
    public NewSessionDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<NewSessionDialogViewModel>(this, vm =>
        {
            // The bool carries "confirmed or cancelled". The dialog itself does not need it —
            // the caller reads the result off the view model — so closing is unconditional.
            void OnCloseRequested(object? sender, bool confirmed) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });
    }
}
