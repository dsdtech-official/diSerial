using CommunityToolkit.Mvvm.Input;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// 通用确认对话框。
/// 监听会话的总线注入警告（M-09 第二层防护）即通过本对话框呈现。
/// </summary>
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

    /// <summary>取消按钮文本为空时隐藏该按钮（用于纯提示场景）。</summary>
    public bool ShowCancel { get; } = !string.IsNullOrEmpty(cancelText);

    public bool IsConfirmed { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Confirm()
    {
        IsConfirmed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsConfirmed = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
