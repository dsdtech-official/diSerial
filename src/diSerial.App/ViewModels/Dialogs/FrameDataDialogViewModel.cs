using CommunityToolkit.Mvvm.Input;

namespace DiSerial.App.ViewModels.Dialogs;

// Present selectable frame data.
public sealed partial class FrameDataDialogViewModel(string title, string data) : ViewModelBase
{
    public string Title { get; } = title;

    public string Data { get; } = data;

    public event EventHandler? CloseRequested;

    // Close the current dialog.
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
