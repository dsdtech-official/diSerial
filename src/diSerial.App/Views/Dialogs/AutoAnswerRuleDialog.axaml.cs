using Avalonia.Controls;
using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Views.Dialogs;

// Edit an automatic reply rule.
public partial class AutoAnswerRuleDialog : Window
{
    // Initialize reply rule controls.
    public AutoAnswerRuleDialog()
    {
        InitializeComponent();

        DialogCloseBinding.Bind<AutoAnswerRuleDialogViewModel>(this, vm =>
        {
            // Close with the requested result.
            void OnCloseRequested(object? sender, EventArgs e) => Close();
            vm.CloseRequested += OnCloseRequested;
            return () => vm.CloseRequested -= OnCloseRequested;
        });

        Opened += (_, _) => this.FindControl<TextBox>("NameBox")?.Focus();
    }
}
