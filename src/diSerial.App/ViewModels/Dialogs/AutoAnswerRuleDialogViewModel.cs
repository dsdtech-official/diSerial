using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Hold an automatic reply draft.
public sealed record AutoAnswerRuleDraft(
    string Name,
    PayloadFormat TriggerFormat, string TriggerText,
    PayloadFormat ResponseFormat, string ResponseText,
    LineEnding Terminator);

// Edit automatic reply rules.
public sealed partial class AutoAnswerRuleDialogViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;

    private string _name = string.Empty;
    private string _triggerText = string.Empty;
    private string _responseText = string.Empty;
    private bool _isTriggerHex;
    private bool _isResponseHex;
    private LineEnding _terminator = LineEnding.None;
    private string? _error;

    // Initialize reply rule editing.
    public AutoAnswerRuleDialogViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        AutoAnswerRule? existing = null)
    {
        _localization = localization;

        Title = localization[existing is null
            ? LocKeys.AutoAnswerDialogTitleAdd
            : LocKeys.AutoAnswerDialogTitleEdit];

        NameLabel = localization[LocKeys.AutoAnswerDialogName];
        TriggerLabel = localization[LocKeys.AutoAnswerDialogTrigger];
        ResponseLabel = localization[LocKeys.AutoAnswerDialogResponse];
        TerminatorLabel = localization[LocKeys.AutoAnswerDialogTerminator];
        ConfirmText = localization[LocKeys.CommonOk];
        CancelText = localization[LocKeys.CommonCancel];

        TerminatorChoices = enumChoices.GetChoices<LineEnding>();

        if (existing is not null)
        {
            _name = existing.Name;
            _triggerText = existing.TriggerText;
            _responseText = existing.ResponseText;
            _isTriggerHex = existing.TriggerFormat == PayloadFormat.Hex;
            _isResponseHex = existing.ResponseFormat == PayloadFormat.Hex;
            _terminator = existing.Terminator;
        }
    }

    public string Title { get; }

    public string NameLabel { get; }

    public string TriggerLabel { get; }

    public string ResponseLabel { get; }

    public string TerminatorLabel { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public IReadOnlyList<LocalizedEnumItem> TerminatorChoices { get; }

    public int MaxNameLength => AutoAnswerLimits.MaxNameLength;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string TriggerText
    {
        get => _triggerText;
        set => SetProperty(ref _triggerText, value);
    }

    public string ResponseText
    {
        get => _responseText;
        set => SetProperty(ref _responseText, value);
    }

    public bool IsTriggerHex
    {
        get => _isTriggerHex;
        set => SetProperty(ref _isTriggerHex, value);
    }

    public bool IsResponseHex
    {
        get => _isResponseHex;
        set => SetProperty(ref _isResponseHex, value);
    }

    public LineEnding Terminator
    {
        get => _terminator;
        set => SetProperty(ref _terminator, value);
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

    public AutoAnswerRuleDraft? Result { get; private set; }

    public event EventHandler? CloseRequested;

    // Accept the dialog values.
    [RelayCommand]
    private void Confirm()
    {
        var name = Name.Trim();

        if (name.Length > AutoAnswerLimits.MaxNameLength)
        {
            Error = _localization[LocKeys.AutoAnswerErrorName];
            return;
        }

        var triggerFormat = IsTriggerHex ? PayloadFormat.Hex : PayloadFormat.Ascii;
        var responseFormat = IsResponseHex ? PayloadFormat.Hex : PayloadFormat.Ascii;

        if (!Encodes(triggerFormat, TriggerText, LineEnding.None))
        {
            Error = _localization[LocKeys.AutoAnswerErrorTrigger];
            return;
        }

        if (!Encodes(responseFormat, ResponseText, Terminator))
        {
            Error = _localization[LocKeys.AutoAnswerErrorResponse];
            return;
        }

        Result = new AutoAnswerRuleDraft(
            name, triggerFormat, TriggerText, responseFormat, ResponseText, Terminator);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Check payload encoding validity.
    private static bool Encodes(PayloadFormat format, string text, LineEnding terminator) =>
        PayloadParser.TryParse(text, format == PayloadFormat.Hex, terminator, out var bytes)
        && bytes.Length > 0
        && bytes.Length <= AutoAnswerLimits.MaxPatternBytes;

    // Cancel the current dialog.
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
