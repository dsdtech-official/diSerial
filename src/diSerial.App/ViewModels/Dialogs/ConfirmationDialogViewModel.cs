using CommunityToolkit.Mvvm.Input;

namespace DiSerial.App.ViewModels.Dialogs;

// Manage confirmation dialogs.
public sealed partial class ConfirmationDialogViewModel(
    string title,
    string message,
    string confirmText,
    string cancelText) : ViewModelBase
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    public string ConfirmText { get; } = confirmText;

    public string CancelText { get; } = cancelText;

    public bool ShowCancel { get; } = !string.IsNullOrEmpty(cancelText);

    public bool IsConfirmed { get; private set; }

    public event EventHandler? CloseRequested;

    // Accept the dialog values.
    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Cancel the current dialog.
    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
