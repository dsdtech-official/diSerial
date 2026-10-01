using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;

namespace DiSerial.App.ViewModels.Dialogs;

// Edit a shortcut name.
public sealed partial class ShortcutNameDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyCollection<string> _existingNames;
    private readonly ILocalizationService _localization;

    private string _name = string.Empty;
    private string? _error;

    // Initialize shortcut name input.
    public ShortcutNameDialogViewModel(
        ILocalizationService localization, IReadOnlyCollection<string> existingNames)
    {
        _localization = localization;
        _existingNames = existingNames;

        Title = localization[LocKeys.ShortcutNameDialogTitle];
        Label = localization[LocKeys.ShortcutNameDialogLabel];
        Hint = localization[LocKeys.ShortcutNameDialogHint];
        ConfirmText = localization[LocKeys.CommonOk];
        CancelText = localization[LocKeys.CommonCancel];
    }

    public string Title { get; }

    public string Label { get; }

    public string Hint { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public int MaxNameLength => ShortcutLimits.MaxNameLength;

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value)) return;

            Error = null;
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string? Result { get; private set; }

    public event EventHandler? CloseRequested;

    // Accept the dialog values.
    [RelayCommand]
    private void Confirm()
    {
        var trimmed = Name.Trim();

        if (trimmed.Length == 0)
        {
            Error = _localization[LocKeys.ShortcutErrorEmpty];
            return;
        }

        if (trimmed.Length > ShortcutLimits.MaxNameLength)
        {
            Error = _localization[LocKeys.ShortcutErrorTooLong];
            return;
        }

        if (_existingNames.Contains(trimmed, StringComparer.Ordinal))
        {
            Error = _localization[LocKeys.ShortcutErrorDuplicate];
            return;
        }

        Result = trimmed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Cancel the current dialog.
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
