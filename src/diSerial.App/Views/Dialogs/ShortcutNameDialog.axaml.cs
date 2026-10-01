using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Choose a shortcut name.
public partial class ShortcutNameDialog : Window
{
    // Initialize shortcut name input.
    public ShortcutNameDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<ShortcutNameDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });

        Opened += (_, _) => this.FindControl<TextBox>("NameBox")?.Focus();
    }
}
