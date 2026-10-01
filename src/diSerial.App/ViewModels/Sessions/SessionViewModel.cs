using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.Views;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Panels;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Sessions;

// Manage session lifecycle.
public abstract partial class SessionViewModel : LocalizedViewModelBase, IAsyncDisposable
{
    private static readonly TimeSpan UiRefreshInterval = TimeSpan.FromMilliseconds(33);

    internal const int MaxFramesPerDrain = 2000;

    private readonly ICaptureSession _capture;
    private readonly ISessionRecorder _recorder;

    private readonly ILogger _logger;

    private readonly ConcurrentQueue<(SerialFrame Frame, string? Alias)> _pending = new();

    private readonly IPeriodicTimer _uiPump;

    private IPeriodicTimer? _reconnectTimer;

    private int _reconnectAttempt;

    private byte[]? _suspendedTimedSend;

    // Initialize shared session state.
    protected SessionViewModel(
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context,
        IShortcutStore? shortcuts)
        : base(context.Localization)
    {
        _capture = capture;
        _recorder = recorder;
        Context = context;

        _logger = context.LoggerFactory.CreateLogger("Session");

        LogPanel = new LogPanelViewModel(context.Formatter, context.EnumChoices)
        {
            MaxFrames = context.Settings.Options.MaxDisplayedFrames
        };
        SendPanel = new SendPanelViewModel(
            context.EnumChoices, context.Timers, context.SendHistory,
            context.SendHistoryScheduler, _logger, shortcuts);
        SendPanel.ReloadShortcuts();

        SignalPanel = capture is IControlLineSession controlLines
            ? new SignalPanelViewModel(controlLines, context.Timers, context.Localization)
            : null;

        SessionLog = new SessionLogViewModel(
            context.SessionLog, context.Timers, context.Localization);
        Subscribe<SendRequestedEventArgs>(OnSendRequested,
            h => SendPanel.SendRequested += h, h => SendPanel.SendRequested -= h);
        Subscribe(OnInputRejected,
            h => SendPanel.InputRejected += h, h => SendPanel.InputRejected -= h);
        Subscribe(OnPayloadMissing,
            h => SendPanel.PayloadMissing += h, h => SendPanel.PayloadMissing -= h);
        Subscribe(OnShortcutAddRequested,
            h => SendPanel.ShortcutAddRequested += h, h => SendPanel.ShortcutAddRequested -= h);
        Subscribe<ShortcutItemViewModel>(OnShortcutRenameRequested,
            h => SendPanel.ShortcutRenameRequested += h,
            h => SendPanel.ShortcutRenameRequested -= h);
        Subscribe(OnIntervalRejected,
            h => SendPanel.IntervalRejected += h, h => SendPanel.IntervalRejected -= h);
        Subscribe(OnClearHistoryRequested,
            h => SendPanel.ClearHistoryRequested += h, h => SendPanel.ClearHistoryRequested -= h);

        Subscribe(OnPreferenceChanged,
            h => LogPanel.PropertyChanged += h, h => LogPanel.PropertyChanged -= h);
        Subscribe<string>(OnFrameDataRequested,
            h => LogPanel.FrameDataRequested += h, h => LogPanel.FrameDataRequested -= h);
        Subscribe(OnPreferenceChanged,
            h => SendPanel.PropertyChanged += h, h => SendPanel.PropertyChanged -= h);

        Subscribe<FrameCapturedEventArgs>(OnFrameCaptured,
            h => _capture.FrameCaptured += h, h => _capture.FrameCaptured -= h);
        Subscribe<ConnectionStateChangedEventArgs>(OnCaptureStateChanged,
            h => _capture.StateChanged += h, h => _capture.StateChanged -= h);
        Subscribe<RecordingFailedEventArgs>(OnRecordingFailed,
            h => _recorder.Failed += h, h => _recorder.Failed -= h);
        Subscribe<SerialErrorEventArgs>(OnLineErrorDetected,
            h => _capture.LineErrorDetected += h, h => _capture.LineErrorDetected -= h);

        _uiPump = context.Timers.Create(
            UiRefreshInterval, DrainPending, TimerPriority.Background);
        _uiPump.Start();
    }


    private readonly List<Action> _unsubscribe = [];

    // Bind a session event handler.
    protected void Subscribe<TArgs>(
        EventHandler<TArgs> handler,
        Action<EventHandler<TArgs>> subscribe,
        Action<EventHandler<TArgs>> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    // Bind a session event handler.
    protected void Subscribe(
        EventHandler handler, Action<EventHandler> subscribe, Action<EventHandler> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    // Bind a session event handler.
    protected void Subscribe(
        PropertyChangedEventHandler handler,
        Action<PropertyChangedEventHandler> subscribe,
        Action<PropertyChangedEventHandler> unsubscribe)
    {
        subscribe(handler);
        _unsubscribe.Add(() => unsubscribe(handler));
    }

    protected SessionContext Context { get; }

    public abstract SessionKind Kind { get; }


    private bool _applyingPreferences;

    // Load saved session preferences.
    protected void ApplyStoredPreferences()
    {
        var prefs = Kind == SessionKind.Monitor
            ? Context.Settings.Monitor
            : Context.Settings.Terminal;

        _applyingPreferences = true;
        try
        {
            LogPanel.DisplayFormat = prefs.Display.Format;
            LogPanel.TimestampMode = prefs.Display.Timestamp;
            LogPanel.ShowChannelColumn = prefs.Display.ShowChannel;
            LogPanel.ShowDeltaColumn = prefs.Display.ShowDelta;
            LogPanel.ShowSentData = prefs.Display.ShowSent;

        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    // Persist changed preferences.
    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPanelViewModel.IsPaused))
        {
            OnPropertyChanged(nameof(PauseButtonText));
            OnPropertyChanged(nameof(PauseButtonIcon));
            OnPropertyChanged(nameof(PauseMenuText));
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        if (_applyingPreferences) return;

        var relevant = e.PropertyName is
            nameof(LogPanelViewModel.DisplayFormat) or
            nameof(LogPanelViewModel.TimestampMode) or
            nameof(LogPanelViewModel.ShowChannelColumn) or
            nameof(LogPanelViewModel.ShowDeltaColumn) or
            nameof(LogPanelViewModel.ShowSentData);

        if (!relevant) return;

        var display = new DisplayPreferences
        {
            Format = LogPanel.DisplayFormat,
            Timestamp = LogPanel.TimestampMode,
            ShowChannel = LogPanel.ShowChannelColumn,
            ShowDelta = LogPanel.ShowDeltaColumn,
            ShowSent = LogPanel.ShowSentData
        };

        if (Kind == SessionKind.Monitor)
        {
            Context.Settings.Monitor = Context.Settings.Monitor with { Display = display };
        }
        else
        {
            Context.Settings.Terminal = Context.Settings.Terminal with { Display = display };
        }
    }

    public abstract string Title { get; }

    public abstract string StatusText { get; }

    public LogPanelViewModel LogPanel { get; }

    public SendPanelViewModel SendPanel { get; }

    public SignalPanelViewModel? SignalPanel { get; }

    public SessionLogViewModel SessionLog { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    [NotifyPropertyChangedFor(nameof(ConnectionButtonText))]
    [NotifyPropertyChangedFor(nameof(ConnectionButtonIcon))]
    private ConnectionState _state = ConnectionState.Disconnected;

    public bool IsConnected => State == ConnectionState.Connected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    [NotifyPropertyChangedFor(nameof(RecordButtonIcon))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isRecording;

    private volatile bool _recordingVisibleToCapture;

    // Refresh recording controls.
    partial void OnIsRecordingChanged(bool value) => _recordingVisibleToCapture = value;

    public long? RecordingBatchId => IsRecording ? _recorder.CurrentBatchId : null;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorNotice))]
    [NotifyPropertyChangedFor(nameof(HasErrorNotice))]
    [NotifyCanExecuteChangedFor(nameof(DismissErrorCommand))]
    private SerialErrorKind? _errorKind;

    public string? ErrorNotice => ErrorKind is { } kind
        ? SessionErrorPresenter.Describe(kind, Localization)
        : null;

    public bool HasErrorNotice => ErrorKind is not null;

    // Clear the visible session error.
    [RelayCommand(CanExecute = nameof(HasErrorNotice))]
    private void DismissError() => ErrorKind = null;

    // Display a session error.
    private void ReportError(SerialErrorKind kind, string? diagnostic = null)
    {
        var replaced = ErrorKind;

        ErrorMessage = diagnostic;
        ErrorKind = kind;

        _logger.LogWarning(
            "Error notice shown: kind={Kind}, session={Session}, replaced={Replaced}, detail={Detail}",
            kind, Kind, replaced?.ToString() ?? "none", diagnostic ?? "-");
    }

    // Connect and report failures.
    public async Task<SessionOpenFailure?> TryConnectAsync()
    {
        try
        {
            ErrorMessage = null;
            ErrorKind = null;
            await _capture.StartAsync();
            return null;
        }
        catch (Exception ex)
        {
            State = ConnectionState.Faulted;

            var failure = SessionOpenFailure.From(ex);

            _logger.LogWarning(
                "Session open failed before the dialog closed: session={Session}, "
                + "channel={Channel}, kind={Kind}, detail={Detail}",
                Kind, failure.Channel, failure.Kind, failure.Detail ?? "-");

            return failure;
        }
    }

    // Connect the session ports.
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        DropSuspendedTimedSend();
        StopAutoReconnect();

        try
        {
            ErrorMessage = null;
            ErrorKind = null;
            await _capture.StartAsync();
        }
        catch (Exception ex)
        {
            ReportError(SerialErrorClassifier.Classify(ex), ex.Message);
            State = ConnectionState.Faulted;
        }
    }

    // Check connection eligibility.
    private bool CanConnect() => State is ConnectionState.Disconnected or ConnectionState.Faulted;

    // Disconnect the session ports.
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task DisconnectAsync()
    {
        DropSuspendedTimedSend();
        StopAutoReconnect();
        await _capture.StopAsync();
    }

    // Toggle the session connection.
    [RelayCommand(CanExecute = nameof(CanToggleConnection))]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected) await DisconnectAsync();
        else await ConnectAsync();
    }

    // Check connection toggle access.
    private bool CanToggleConnection() => CanConnect() || IsConnected;

    public string ConnectionButtonText => L(IsConnected ? StopKey : StartKey);

    public Geometry ConnectionButtonIcon =>
        IsConnected ? ToolbarIcons.Disconnect : ToolbarIcons.Connect;

    protected virtual string StartKey => LocKeys.ToolbarConnect;

    protected virtual string StopKey => LocKeys.ToolbarDisconnect;

    // Clear session display data.
    [RelayCommand]
    private void Clear()
    {
        LogPanel.ClearCommand.Execute(null);
        OnCleared();

        OnPropertyChanged(nameof(StatusText));
    }

    // Handle cleared session data.
    protected virtual void OnCleared() { }

    // Toggle the display pause state.
    [RelayCommand]
    private void TogglePause() => LogPanel.TogglePauseCommand.Execute(null);

    // Toggle session recording.
    [RelayCommand]
    private async Task ToggleRecordAsync()
    {
        if (IsRecording)
        {
            var batchId = _recorder.CurrentBatchId;
            var frames = _recorder.FramesWritten;

            await _recorder.StopAsync();
            IsRecording = false;

            if (batchId is { } id && frames > 0) await ExportBatchAsync(id);
            return;
        }

        try
        {
            await _recorder.StartAsync(DescribeRecordingBatch());
            IsRecording = true;
        }
        catch (Exception ex)
        {
            ReportError(SerialErrorKind.RecordingFailed, ex.Message);
            _logger.LogError(ex, "Starting the recording failed.");
            IsRecording = false;
        }
    }

    // Describe the recorded batch.
    protected abstract RecordingBatchInfo DescribeRecordingBatch();

    // Report a serial line error.
    private void OnLineErrorDetected(object? sender, SerialErrorEventArgs e)
        => Dispatcher.UIThread.Post(() => ReportError(e.Kind, e.Message));

    // Report a recording failure.
    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRecording = false;
            ReportError(SerialErrorKind.RecordingFailed, e.Exception.Message);
        });

        _ = ReleaseRecorderAfterFailureAsync();
    }

    // Release a failed recorder.
    private async Task ReleaseRecorderAfterFailureAsync()
    {
        try
        {
            await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Releasing the recorder after a write failure failed.");
        }
    }

    // Export the selected frame data.
    [RelayCommand]
    private Task ExportAsync()
    {
        var snapshot = LogPanel.Frames
            .Select(f => f.Frame)
            .OrderBy(f => f.Sequence)
            .ToArray();

        return RunExportAsync(
            DescribeExportBaseName("export"),
            ChannelsForExport(),
            _ => ToAsync(snapshot));
    }

    // Stream a frame list for export.
    private static async IAsyncEnumerable<SerialFrame> ToAsync(IEnumerable<SerialFrame> frames)
    {
        foreach (var f in frames) yield return f;
        await Task.CompletedTask;
    }

    // Export a recorded batch.
    private async Task ExportBatchAsync(long batchId)
    {
        var summary = await Context.RecordingReader.GetBatchAsync(batchId);
        var baseName = summary is null
            ? DescribeExportBaseName("record")
            : RecordingExportName.ForBatch(summary);

        await RunExportAsync(
            baseName, ChannelsForExport(),
            ct => Context.RecordingReader.ReadBatchAsync(batchId, ct));
    }

    // Select exported channels.
    private ExportChannels ChannelsForExport() => new(
        ResolveChannelPort(ChannelId.A), ResolveChannelAlias(ChannelId.A),
        ResolveChannelPort(ChannelId.B), ResolveChannelAlias(ChannelId.B));

    // Run the frame export operation.
    private async Task RunExportAsync(
        string suggestedBaseName,
        ExportChannels channels,
        Func<CancellationToken, IAsyncEnumerable<SerialFrame>> source)
    {
        using var vm = new ExportDialogViewModel(
            Context.Localization,
            Context.EnumChoices,
            Context.DialogService,
            new ExportDialogSeed(
                suggestedBaseName,
                LogPanel.DisplayFormat,
                LogPanel.TimestampMode,
                LogPanel.ShowChannelColumn,
                LogPanel.ShowDeltaColumn,
                channels,
                ResolveExportDirectory(),
                Context.Settings.Options.LastExportFormat));

        var request = await Context.DialogService.ShowExportDialogAsync(vm);
        if (request is null) return;

        try
        {
            var frames = new List<SerialFrame>();
            await foreach (var f in source(CancellationToken.None)) frames.Add(f);

            await Context.ExportService.ExportAsync(frames, request.FilePath, request.Options);
            _logger.LogInformation(
                "Exported {Count} frames to {Path}", frames.Count, request.FilePath);

            Context.Settings.Options = Context.Settings.Options with
            {
                LastExportFormat = request.Options.Format
            };

            var directory = Path.GetDirectoryName(request.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Context.Settings.LastExportDirectory = directory;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export to {Path} failed.", request.FilePath);
            ReportError(SerialErrorKind.Unknown, ex.Message);
        }
    }

    // Choose the export directory.
    private string? ResolveExportDirectory()
    {
        var remembered = Context.Settings.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(remembered)) return null;

        if (Directory.Exists(remembered)) return remembered;

        _logger.LogInformation(
            "Last export directory {Path} is gone; the export dialog falls back to Documents.",
            remembered);
        return null;
    }

    // Build the export base name.
    protected virtual string DescribeExportBaseName(string kind) =>
        $"diserial-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}";

    // Refresh localized display text.
    protected override void OnCultureChanged()
    {
        LogPanel.RefreshAll();
        base.OnCultureChanged();
    }

    // Describe connection status.
    protected virtual string DescribeConnectionState() => IsReconnecting
        ? LF(LocKeys.StateReconnecting, ReconnectAttempt, Context.Settings.Options.ReconnectAttempts)
        : _reconnectAttempt > 0
            ? LF(LocKeys.StateReconnectFailed, _reconnectAttempt)
            : DescribePlainConnectionState();

    // Describe the connection state.
    protected virtual string DescribePlainConnectionState() => State switch
    {
        ConnectionState.Connected => L(LocKeys.StateConnected),
        ConnectionState.Connecting => L(LocKeys.StateConnecting),
        ConnectionState.Faulted => L(LocKeys.StateFaulted),
        _ => L(LocKeys.StateDisconnected)
    };

    // Describe the session state.
    protected string DescribeState()
    {
        var state = DescribeConnectionState();

        if (IsRecording) state = LF(LocKeys.StateRecording, state);

        if (LogPanel.IsPaused) state = LF(LocKeys.StateDisplayPaused, state);

        return state;
    }

    public string PauseButtonText =>
        L(LogPanel.IsPaused ? LocKeys.ToolbarResume : LocKeys.ToolbarPause);

    public Geometry PauseButtonIcon =>
        LogPanel.IsPaused ? ToolbarIcons.Resume : ToolbarIcons.Pause;

    public Geometry RecordButtonIcon =>
        IsRecording ? ToolbarIcons.StopRecord : ToolbarIcons.Record;

    public string PauseMenuText =>
        L(LogPanel.IsPaused ? LocKeys.MenuSessionResume : LocKeys.MenuSessionPause);

    public string RecordButtonText =>
        L(IsRecording ? LocKeys.ToolbarStopRecord : LocKeys.ToolbarRecord);

    // Get a frame channel alias.
    protected virtual string? ResolveChannelAlias(ChannelId channel) => null;

    // Get a frame channel port.
    protected virtual string? ResolveChannelPort(ChannelId channel) => null;

    // Get a frame channel label.
    protected virtual string? ResolveChannelLabel(ChannelId channel) => null;

    // Send the requested payload.
    protected virtual async Task SendAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, bool asAutoAnswer = false)
    {
        if (asAutoAnswer && _capture is IAutoAnswerSession auto)
        {
            await auto.SendAutoAnswerAsync(channel, data);
            return;
        }

        await _capture.SendAsync(channel, data);
    }

    // Queue a captured frame.
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        _pending.Enqueue((e.Frame, ResolveChannelLabel(e.Frame.Channel)));

        if (_recordingVisibleToCapture) _ = _recorder.WriteAsync(e.Frame);
    }

    // Show the requested frame data.
    private async void OnFrameDataRequested(object? sender, string data)
    {
        try
        {
            await Context.DialogService.ShowFrameDataAsync(L(LocKeys.FrameDataTitle), data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not show the frame data window.");
        }
    }

    // Display pending captured frames.
    private void DrainPending()
    {
        if (_pending.IsEmpty) return;

        var batch = new List<(SerialFrame Frame, string? Alias)>();
        while (batch.Count < MaxFramesPerDrain && _pending.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        OnFramesAppended(batch);

        LogPanel.AppendRange(batch);
        OnPropertyChanged(nameof(StatusText));
    }

    // Handle newly displayed frames.
    protected virtual void OnFramesAppended(
        IReadOnlyList<(SerialFrame Frame, string? Alias)> batch) { }

    // Update timed sending state.
    partial void OnStateChanged(ConnectionState value)
    {
        if (value != ConnectionState.Connected)
        {
            StopTimedSendOnDisconnect();
            return;
        }

        ResumeTimedSendIfSuspended();
    }

    // Stop sending after disconnect.
    private void StopTimedSendOnDisconnect() => SendPanel.StopTimedSend();

    public bool IsReconnecting => _reconnectTimer is not null;

    public int ReconnectAttempt => _reconnectAttempt;

    // Start automatic reconnection.
    internal void BeginAutoReconnect()
    {
        if (_reconnectTimer is not null) return;
        if (!Context.Settings.Options.AutoReconnect) return;

        var options = Context.Settings.Options;

        _reconnectAttempt = 1;

        _logger.LogInformation(
            "Auto-reconnect armed for {Kind} session: up to {Attempts} attempt(s), {Delay} ms apart",
            Kind, options.ReconnectAttempts, options.ReconnectDelayMs);

        _reconnectTimer = Context.Timers.Create(
            TimeSpan.FromMilliseconds(options.ReconnectDelayMs),
            () => _ = TryReconnectOnceAsync());
        _reconnectTimer.Start();

        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(StatusText));
    }

    // Attempt one reconnection.
    private async Task TryReconnectOnceAsync()
    {
        if (_reconnectTimer is null) return;

        var limit = Context.Settings.Options.ReconnectAttempts;

        _logger.LogInformation(
            "Auto-reconnect attempt {Attempt}/{Limit} for {Kind} session",
            _reconnectAttempt, limit, Kind);

        try
        {
            await _capture.StartAsync();

            StopAutoReconnect();
            ErrorMessage = null;
            ErrorKind = null;
            _logger.LogInformation("Auto-reconnect succeeded for {Kind} session",
                Kind);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Auto-reconnect attempt {Attempt}/{Limit} failed: {Reason}",
                _reconnectAttempt, limit, ex.Message);
        }

        if (_reconnectAttempt < limit)
        {
            _reconnectAttempt++;
            OnPropertyChanged(nameof(ReconnectAttempt));
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        var made = _reconnectAttempt;
        DropSuspendedTimedSend();
        StopAutoReconnect();
        _reconnectAttempt = made;
        _logger.LogWarning(
            "Auto-reconnect gave up for {Kind} session after {Attempts} attempt(s); waiting for the user",
            Kind, made);
        OnPropertyChanged(nameof(ReconnectAttempt));
        OnPropertyChanged(nameof(StatusText));
    }

    // Pause sending for reconnection.
    internal void SuspendTimedSendForReconnect()
    {
        if (!Context.Settings.Options.AutoReconnect) return;

        if (_suspendedTimedSend is not null) return;

        _suspendedTimedSend = SendPanel.SuspendTimedSend();
    }

    // Resume a suspended timed send.
    private void ResumeTimedSendIfSuspended()
    {
        if (_suspendedTimedSend is not { } payload) return;

        if (State != ConnectionState.Connected) return;

        _suspendedTimedSend = null;
        SendPanel.ResumeTimedSend(payload);
        _logger.LogInformation("Timed send resumed after reconnect for {Kind} session", Kind);
    }

    // Discard paused timed sending.
    private void DropSuspendedTimedSend() => _suspendedTimedSend = null;

    internal bool HasSuspendedTimedSend => _suspendedTimedSend is not null;

    // Cancel automatic reconnection.
    private void StopAutoReconnect()
    {
        if (_reconnectTimer is null) return;

        _reconnectTimer.Stop();
        _reconnectTimer.Dispose();
        _reconnectTimer = null;
        _reconnectAttempt = 0;

        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(ReconnectAttempt));
        OnPropertyChanged(nameof(StatusText));
    }

    // Handle capture state changes.
    private void OnCaptureStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            if (e.State == ConnectionState.Faulted) SuspendTimedSendForReconnect();

            State = e.State;

            if (e.State == ConnectionState.Faulted && e.ErrorKind != SerialErrorKind.Unknown)
            {
                ReportError(e.ErrorKind, e.Message);
            }

            if (e.State == ConnectionState.Faulted) BeginAutoReconnect();

            if (e.State == ConnectionState.Connected) StopAutoReconnect();
        });

    // Write bytes to the port.
    protected async Task<WireWriteOutcome> WriteToTheWireAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, bool asAutoAnswer = false)
    {
        if (State != ConnectionState.Connected)
        {
            ReportError(SerialErrorKind.NotConnected);
            return WireWriteOutcome.NotConnected;
        }

        try
        {
            await SendAsync(channel, data, asAutoAnswer);
            return WireWriteOutcome.Sent;
        }
        catch (Exception ex)
        {
            ReportError(SerialErrorClassifier.Classify(ex), ex.Message);
            return WireWriteOutcome.Failed;
        }
    }

    // Dispatch a requested payload.
    private async void OnSendRequested(object? sender, SendRequestedEventArgs e)
    {
        var outcome = await WriteToTheWireAsync(e.Channel, e.Data);

        if (outcome == WireWriteOutcome.NotConnected) return;

        if (outcome == WireWriteOutcome.Sent)
        {

            SendPanel.ConfirmSent(e.SourceText);
            return;
        }

        SendPanel.StopTimedSend();
    }

    // Add the requested shortcut.
    private async void OnShortcutAddRequested(object? sender, EventArgs e)
    {
        var existing = SendPanel.Shortcuts.Select(s => s.Name).ToArray();
        var name = await Context.DialogService.ShowShortcutNameDialogAsync(existing);
        if (name is null) return;

        var message = SendPanel.AddShortcut(name) switch
        {
            ShortcutAddResult.Full => LocKeys.ShortcutFull,
            ShortcutAddResult.StorageFailed => LocKeys.ShortcutErrorStorage,
            _ => null
        };

        if (message is not null)
        {
            await Context.DialogService.ShowMessageAsync(
                Context.Localization[LocKeys.ShortcutAddButton],
                Context.Localization[message]);
        }
    }

    // Rename the requested shortcut.
    private async void OnShortcutRenameRequested(object? sender, ShortcutItemViewModel item)
    {
        var existing = SendPanel.Shortcuts
            .Where(s => s.Id != item.Id)
            .Select(s => s.Name)
            .ToArray();

        var name = await Context.DialogService.ShowShortcutNameDialogAsync(existing);
        if (name is not null) SendPanel.RenameShortcut(item.Id, name);
    }

    // Report invalid send input.
    private void OnInputRejected(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.InvalidInput);

    // Report an empty send payload.
    private void OnPayloadMissing(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.NothingToSend);

    // Report an invalid send interval.
    private void OnIntervalRejected(object? sender, EventArgs e)
        => ReportError(SerialErrorKind.IntervalTooSmall);

    // Confirm clearing send history.
    private async void OnClearHistoryRequested(object? sender, EventArgs e)
    {
        var count = await SendPanel.GetStoredHistoryCountAsync();
        if (count == 0) return;

        var confirmed = await Context.DialogService.ShowConfirmationAsync(
            L(LocKeys.SendHistoryClearTitle),
            LF(LocKeys.SendHistoryClearMessage, count),
            L(LocKeys.SendHistoryClearConfirm),
            L(LocKeys.CommonCancel));

        if (!confirmed) return;

        try
        {
            await SendPanel.ConfirmClearHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clearing the send history failed");
            await Context.DialogService.ShowMessageAsync(
                L(LocKeys.SendHistoryClearTitle), L(LocKeys.SendHistoryClearFailed));
        }
    }

    // Close the session and its ports.
    public async ValueTask DisposeAsync()
    {
        _uiPump.Stop();
        _uiPump.Dispose();

        SignalPanel?.Dispose();

        SessionLog.Dispose();

        SendPanel.StopTimedSend();

        await _capture.DisposeAsync();

        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();

        Dispose();

        await _recorder.DisposeAsync();
    }
}

public enum WireWriteOutcome
{
    Sent,
    NotConnected,
    Failed
}
