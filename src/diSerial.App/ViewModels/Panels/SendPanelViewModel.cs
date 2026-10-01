using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.ViewModels.Panels;

// Manage serial transmission.
public sealed partial class SendPanelViewModel : ViewModelBase
{
    private const int MaxHistory = 12;

    private readonly IShortcutStore? _shortcuts;

    // Initialize send panel services.
    public SendPanelViewModel(
        IEnumChoiceProvider enumChoices,
        IPeriodicTimerFactory timers,
        ISendHistoryStore history,
        ISendHistoryScheduler scheduler,
        ILogger? logger = null,
        IShortcutStore? shortcuts = null)
    {
        _shortcuts = shortcuts;

        LineEndingChoices = enumChoices.GetChoices<LineEnding>();
        _timers = timers;
        _history = history;
        _scheduler = scheduler;
        _logger = logger ?? NullLogger.Instance;

        _ = ReloadHistoryAsync();
    }

    private readonly IPeriodicTimerFactory _timers;

    private readonly ISendHistoryStore _history;

    private readonly ISendHistoryScheduler _scheduler;

    private readonly ILogger _logger;

    public event EventHandler<SendRequestedEventArgs>? SendRequested;

    public event EventHandler? EnableSendRequested;

    public event EventHandler? ClearHistoryRequested;

    public event EventHandler? InputRejected;

    public IReadOnlyList<LocalizedEnumItem> LineEndingChoices { get; }

    [ObservableProperty]
    private string _input = string.Empty;

    // Update send input state.
    partial void OnInputChanged(string value)
    {
        if (!_applyingHistory) _historyCursor = NotBrowsing;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditLineEnding))]
    private bool _isHexMode;

    [ObservableProperty]
    private LineEnding _lineEnding = LineEnding.None;

    public bool CanEditLineEnding => !IsHexMode && CanEditSendContent;

    // Apply the selected input mode.
    partial void OnIsHexModeChanged(bool value)
    {
        if (value) LineEnding = LineEnding.None;
    }

    // Apply the selected terminator.
    partial void OnLineEndingChanged(LineEnding value)
    {
        if (IsHexMode && value != LineEnding.None) LineEnding = LineEnding.None;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInjectionWarning))]
    private bool _isSendEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInjectionWarning))]
    private bool _isMonitorSession;

    public bool ShowInjectionWarning => IsMonitorSession && IsSendEnabled;

    public ObservableCollection<ChannelViewModel> SendTargets { get; } = [];

    public bool HasSendTargets => SendTargets.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetChannel))]
    private ChannelViewModel? _selectedTarget;

    public ChannelId TargetChannel => SelectedTarget?.Id ?? ChannelId.None;

    public ObservableCollection<SendHistoryItemViewModel> History { get; } = [];

    [ObservableProperty]
    private SendHistoryItemViewModel? _selectedHistoryEntry;

    // Apply the selected send history.
    partial void OnSelectedHistoryEntryChanged(SendHistoryItemViewModel? value)
    {
        if (value is null) return;

        SelectedHistoryEntry = null;

        if (!CanEditSendContent) return;

        if (value.IsClearAction)
        {
            ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsHexMode = value.IsHexMode;
        Input = value.Text;
    }


    private const int NotBrowsing = -1;

    private int _historyCursor = NotBrowsing;

    private bool _applyingHistory;

    private int LastEntryIndex =>
        History.Count == 0 ? NotBrowsing
        : History[^1].IsClearAction ? History.Count - 2
        : History.Count - 1;

    // Select an older history entry.
    [RelayCommand]
    private void HistoryOlder() => MoveHistory(+1);

    // Select a newer history entry.
    [RelayCommand]
    private void HistoryNewer() => MoveHistory(-1);

    // Move through send history.
    private void MoveHistory(int delta)
    {
        if (!CanEditSendContent) return;

        var last = LastEntryIndex;
        if (last < 0) return;

        var target = _historyCursor + delta;
        if (target < 0 || target > last) return;

        _historyCursor = target;
        ApplyHistoryEntry(History[target]);
    }

    // Load a saved send entry.
    private void ApplyHistoryEntry(SendHistoryItemViewModel entry)
    {
        _applyingHistory = true;
        try
        {
            IsHexMode = entry.IsHexMode;
            Input = entry.Text;
        }
        finally
        {
            _applyingHistory = false;
        }
    }

    // Count stored send entries.
    public Task<int> GetStoredHistoryCountAsync() => _scheduler.RunAsync(_history.Count);

    // Confirm clearing send history.
    public async Task ConfirmClearHistoryAsync()
    {
        await _scheduler.RunAsync(_history.Clear);
        await ReloadHistoryAsync();
    }

    // Remove a saved send entry.
    private async void RemoveHistoryEntry(SendHistoryItemViewModel item)
    {
        await _scheduler.RunAsync(() => _history.Delete(item.Text, item.IsHexMode));
        await ReloadHistoryAsync();
    }

    // Reload saved send history.
    private async Task ReloadHistoryAsync()
    {
        var entries = await _scheduler.RunAsync(() => _history.Load(MaxHistory));

        History.Clear();

        foreach (var entry in entries)
        {
            History.Add(new SendHistoryItemViewModel(entry.Text, entry.IsHexMode, RemoveHistoryEntry));
        }

        if (History.Count > 0) History.Add(SendHistoryItemViewModel.ClearAction());
    }

    // Request a payload transmission.
    [RelayCommand]
    private void Send()
    {
        if (!IsSendEnabled || string.IsNullOrEmpty(Input)) return;

        if (!CanEditSendContent) return;

        if (!PayloadParser.TryParse(Input, IsHexMode, LineEnding, out var payload))
        {
            _logger.LogDebug("Rejected the send input: it does not parse in the current mode.");
            InputRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        SendRequested?.Invoke(this, new SendRequestedEventArgs(TargetChannel, payload, Input));
        PushHistory(Input);
    }

    // Build a saved command payload.
    public byte[]? TryBuildShortcutPayload()
    {
        if (string.IsNullOrEmpty(Input)) return null;

        if (!PayloadParser.TryParse(Input, IsHexMode, LineEnding, out var payload))
        {
            _logger.LogDebug("Rejected the send input: it does not parse in the current mode.");
            InputRejected?.Invoke(this, EventArgs.Empty);
            return null;
        }

        return payload;
    }


    public ObservableCollection<ShortcutItemViewModel> Shortcuts { get; } = [];

    public bool HasShortcuts => Shortcuts.Count > 0;

    public bool CanCreateShortcuts => _shortcuts is not null;

    public event EventHandler? ShortcutSendBlocked;

    // Reload saved send shortcuts.
    public void ReloadShortcuts()
    {
        Shortcuts.Clear();
        if (_shortcuts is null) { OnPropertyChanged(nameof(HasShortcuts)); return; }

        foreach (var item in _shortcuts.Load()) Shortcuts.Add(new ShortcutItemViewModel(item, this));
        OnPropertyChanged(nameof(HasShortcuts));
    }

    // Save a new send shortcut.
    public ShortcutAddResult AddShortcut(string name)
    {
        if (_shortcuts is null) return ShortcutAddResult.EmptyPayload;

        var payload = TryBuildShortcutPayload();
        if (payload is null) return ShortcutAddResult.EmptyPayload;

        var result = _shortcuts.Add(name, payload);
        if (result == ShortcutAddResult.Added) ReloadShortcuts();
        return result;
    }

    // Change a send shortcut name.
    public bool RenameShortcut(long id, string name)
    {
        if (_shortcuts is null) return false;

        var ok = _shortcuts.Rename(id, name);
        if (ok) ReloadShortcuts();
        return ok;
    }

    public event EventHandler? ShortcutAddRequested;

    // Request a new shortcut.
    [RelayCommand]
    public void RequestAddShortcut()
    {
        if (string.IsNullOrEmpty(Input))
        {
            PayloadMissing?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (TryBuildShortcutPayload() is null) return;

        ShortcutAddRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<ShortcutItemViewModel>? ShortcutRenameRequested;

    // Request a shortcut rename.
    [RelayCommand]
    public void RenameShortcutRequested(ShortcutItemViewModel item) =>
        ShortcutRenameRequested?.Invoke(this, item);

    // Delete a saved send shortcut.
    [RelayCommand]
    public void DeleteShortcut(ShortcutItemViewModel item)
    {
        _shortcuts?.Delete(item.Id);
        ReloadShortcuts();
    }

    // Move a shortcut to first place.
    [RelayCommand]
    public void MoveShortcutToFront(ShortcutItemViewModel item)
    {
        _shortcuts?.MoveToFront(item.Id);
        ReloadShortcuts();
    }

    // Move a shortcut one place up.
    [RelayCommand]
    public void MoveShortcutForwardOne(ShortcutItemViewModel item)
    {
        _shortcuts?.MoveForwardOne(item.Id);
        ReloadShortcuts();
    }

    // Send a saved shortcut payload.
    [RelayCommand]
    public void SendShortcut(ShortcutItemViewModel item)
    {
        if (!IsSendEnabled)
        {
            ShortcutSendBlocked?.Invoke(this, EventArgs.Empty);
            return;
        }

        SendRequested?.Invoke(this, new SendRequestedEventArgs(TargetChannel, item.Payload));
    }

    // Record a successful send.
    public void ConfirmSent(string? sentText)
    {
        if (sentText is null) return;

        if (Input == sentText) Input = string.Empty;
    }

    // Send from the Return key.
    [RelayCommand]
    private void SendFromEnter()
    {
        if (IsMonitorSession) return;
        Send();
    }


    public const int MinIntervalMs = 20;

    public const int DefaultIntervalMs = 1000;

    private IPeriodicTimer? _timedSend;

    private byte[]? _timedPayload;

    public event EventHandler? IntervalRejected;

    public event EventHandler? PayloadMissing;

    [ObservableProperty]
    private int _intervalMs = DefaultIntervalMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSendContent))]
    [NotifyPropertyChangedFor(nameof(CanEditLineEnding))]
    private bool _isTimedSendRunning;

    public bool CanEditSendContent => !IsTimedSendRunning;

    // Start periodic payload sending.
    [RelayCommand]
    private void StartTimedSend()
    {
        if (IsTimedSendRunning) return;

        if (!IsSendEnabled || IsMonitorSession) return;

        if (IntervalMs < MinIntervalMs)
        {
            IntervalRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (string.IsNullOrEmpty(Input))
        {
            PayloadMissing?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!PayloadParser.TryParse(Input, IsHexMode, LineEnding, out var payload))
        {
            _logger.LogDebug("Rejected the send input: it does not parse in the current mode.");
            InputRejected?.Invoke(this, EventArgs.Empty);
            return;
        }

        _timedPayload = payload;
        IsTimedSendRunning = true;

        _timedSend = _timers.Create(TimeSpan.FromMilliseconds(IntervalMs), FireTimedSend);
        _timedSend.Start();

        PushHistory(Input);

        FireTimedSend();
    }

    // Stop periodic payload sending.
    public void StopTimedSend()
    {
        _timedSend?.Stop();
        _timedSend?.Dispose();
        _timedSend = null;
        _timedPayload = null;
        IsTimedSendRunning = false;
    }

    // Stop sending from the UI.
    [RelayCommand]
    private void StopTimedSendFromUi() => StopTimedSend();

    // Suspend periodic sending.
    public byte[]? SuspendTimedSend()
    {
        if (!IsTimedSendRunning || _timedPayload is null) return null;

        var frozen = _timedPayload;
        StopTimedSend();
        return frozen;
    }

    // Resume suspended sending.
    public void ResumeTimedSend(byte[] payload)
    {
        if (IsTimedSendRunning) return;
        if (!IsSendEnabled || IsMonitorSession) return;

        _timedPayload = payload;
        IsTimedSendRunning = true;

        _timedSend = _timers.Create(TimeSpan.FromMilliseconds(IntervalMs), FireTimedSend);
        _timedSend.Start();

        FireTimedSend();
    }

    // Request the next timed send.
    private void FireTimedSend()
    {
        if (!IsTimedSendRunning || _timedPayload is null) return;

        SendRequested?.Invoke(this, new SendRequestedEventArgs(TargetChannel, _timedPayload));
    }

    // Request monitoring send access.
    [RelayCommand]
    private void RequestEnableSend() => EnableSendRequested?.Invoke(this, EventArgs.Empty);

    // Apply monitoring send approval.
    public void ConfirmEnableSend() => IsSendEnabled = true;

    // Save a successful send entry.
    private void PushHistory(string text)
    {
        var isHex = IsHexMode;
        var lineEnding = LineEnding.ToString();

        _scheduler.Post(() => _history.Record(text, isHex, lineEnding), "record");

        var existing = IndexOf(text, isHex);
        if (existing >= 0)
        {
            if (existing != 0) History.Move(existing, 0);
            return;
        }

        History.Insert(0, new SendHistoryItemViewModel(text, isHex, RemoveHistoryEntry));

        if (!History[^1].IsClearAction) History.Add(SendHistoryItemViewModel.ClearAction());

        while (History.Count - 1 > MaxHistory) History.RemoveAt(History.Count - 2);
    }

    // Locate a send history entry.
    private int IndexOf(string text, bool isHexMode)
    {
        for (var i = 0; i < History.Count; i++)
        {
            if (!History[i].IsClearAction
                && History[i].IsHexMode == isHexMode
                && string.Equals(History[i].Text, text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

}

// Represent a sent command.
public sealed partial class SendHistoryItemViewModel(
    string text, bool isHexMode, Action<SendHistoryItemViewModel>? delete) : ObservableObject
{
    // Create a history clear action.
    public static SendHistoryItemViewModel ClearAction() => new(string.Empty, false, null)
    {
        IsClearAction = true
    };

    public bool IsClearAction { get; private init; }

    public string Text { get; } = text;

    public bool IsHexMode { get; } = isHexMode;

    public bool IsAsciiMode => !IsHexMode && !IsClearAction;

    public bool IsEntry => !IsClearAction;

    // Delete the selected stored item.
    [RelayCommand]
    private void Delete() => delete?.Invoke(this);
}


// Carry a requested payload.
public sealed class SendRequestedEventArgs(
    ChannelId channel, ReadOnlyMemory<byte> data, string? sourceText = null) : EventArgs
{
    public ChannelId Channel { get; } = channel;

    public ReadOnlyMemory<byte> Data { get; } = data;

    public string? SourceText { get; } = sourceText;
}
