using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;

namespace DiSerial.App.ViewModels.Dialogs;

// Edit application options.
public sealed partial class OptionsDialogViewModel : LocalizedViewModelBase
{
    private readonly AppOptions _original;

    // Load editable app options.
    public OptionsDialogViewModel(ILocalizationService localization, AppOptions current)
        : base(localization)
    {
        _original = current;

        AutoReconnect = current.AutoReconnect;
        ReconnectAttempts = current.ReconnectAttempts.ToString();
        ReconnectDelayMs = current.ReconnectDelayMs.ToString();
        MaxDisplayedFrames = current.MaxDisplayedFrames.ToString();
        IdleGapAuto = current.IdleGapAuto;
        IdleGapMs = current.IdleGapMs.ToString();
    }

    [ObservableProperty]
    private bool _autoReconnect;

    [ObservableProperty]
    private string _reconnectAttempts = string.Empty;

    [ObservableProperty]
    private string _reconnectDelayMs = string.Empty;

    [ObservableProperty]
    private string _maxDisplayedFrames = string.Empty;

    [ObservableProperty]
    private bool _idleGapAuto;

    [ObservableProperty]
    private string _idleGapMs = string.Empty;

    public AppOptions? Result { get; private set; }

    // Read saved application options.
    private static int Read(string text, int fallback, Func<int, int> clamp) =>
        int.TryParse(text, out var n) ? clamp(n) : fallback;

    public event EventHandler? CloseRequested;

    // Save the edited app options.
    [RelayCommand]
    private void Accept()
    {
        Result = _original with
        {
            AutoReconnect = AutoReconnect,
            ReconnectAttempts = Read(ReconnectAttempts, _original.ReconnectAttempts, AppOptions.ClampAttempts),
            ReconnectDelayMs = Read(ReconnectDelayMs, _original.ReconnectDelayMs, AppOptions.ClampDelayMs),
            MaxDisplayedFrames = Read(MaxDisplayedFrames, _original.MaxDisplayedFrames, AppOptions.ClampFrames),
            IdleGapAuto = IdleGapAuto,
            IdleGapMs = Read(IdleGapMs, _original.IdleGapMs, AppOptions.ClampIdleGapMs)
        };

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Discard the edited app options.
    [RelayCommand]
    private void Reject()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public string Title => L(LocKeys.OptionsTitle);
    public string GroupConnection => L(LocKeys.OptionsGroupConnection);
    public string AutoReconnectLabel => L(LocKeys.OptionsAutoReconnect);
    public string AttemptsLabel => L(LocKeys.OptionsAttempts);
    public string DelayLabel => L(LocKeys.OptionsDelay);
    public string ReconnectNote => L(LocKeys.OptionsReconnectNote);
    public string GroupDisplay => L(LocKeys.OptionsGroupDisplay);
    public string MaxFramesLabel => L(LocKeys.OptionsMaxFrames);
    public string GroupReception => L(LocKeys.OptionsGroupReception);
    public string IdleGapAutoLabel => L(LocKeys.OptionsIdleGapAuto);
    public string IdleGapLabel => L(LocKeys.OptionsIdleGap);
    public string IdleGapNote => L(LocKeys.OptionsIdleGapNote);
    public string OkText => L(LocKeys.CommonOk);
    public string CancelText => L(LocKeys.CommonCancel);
}
