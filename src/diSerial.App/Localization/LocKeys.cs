namespace DiSerial.App.Localization;

// Define application text keys.
public static class LocKeys
{
    public const string CommonCancel = "Common.Cancel";
    public const string CommonOk = "Common.Ok";

    public const string AppName = "App.Name";
    public const string AppTitleWithSession = "App.TitleWithSession";
    public const string AppTitleDeveloperMode = "App.TitleDeveloperMode";

    public const string MenuLanguage = "Menu.Language";

    public const string MenuHelpAbout = "Menu.Help.About";

    public const string MenuFileNewSession = "Menu.File.NewSession";
    public const string MenuFileNewSessionBlocked = "Menu.File.NewSession.Blocked";

    public const string SessionTerminalTitle = "Session.Terminal.Title";
    public const string SessionMonitorTitle = "Session.Monitor.Title";
    public const string SessionTerminalStatus = "Session.Terminal.Status";
    public const string SessionMonitorStatus = "Session.Monitor.Status";

    public const string StateConnected = "State.Connected";
    public const string StateConnecting = "State.Connecting";
    public const string StateFaulted = "State.Faulted";
    public const string StateDisconnected = "State.Disconnected";
    public const string StateMonitoring = "State.Monitoring";

    public const string StateDisplayPaused = "State.DisplayPaused";

    public const string StateRecording = "State.Recording";
    public const string StateNotStarted = "State.NotStarted";

    public const string ToolbarPause = "Toolbar.Pause";
    public const string ToolbarResume = "Toolbar.Resume";

    public const string ToolbarRecord = "Toolbar.Record";
    public const string ToolbarStopRecord = "Toolbar.StopRecord";

    public const string ToolbarConnect = "Toolbar.Connect";
    public const string ToolbarDisconnect = "Toolbar.Disconnect";
    public const string ToolbarStartMonitoring = "Toolbar.StartMonitoring";
    public const string ToolbarStopMonitoring = "Toolbar.StopMonitoring";

    public const string MenuSessionPause = "Menu.Session.Pause";
    public const string MenuSessionResume = "Menu.Session.Resume";


    public const string PlatformMacOsNotImplemented = "Platform.MacOsNotImplemented";
    public const string PlatformUnknown = "Platform.Unknown";

    public const string ShortcutNameDialogTitle = "Shortcut.NameDialog.Title";
    public const string ShortcutNameDialogLabel = "Shortcut.NameDialog.Label";
    public const string ShortcutNameDialogHint = "Shortcut.NameDialog.Hint";
    public const string ShortcutErrorEmpty = "Shortcut.Error.Empty";
    public const string ShortcutErrorTooLong = "Shortcut.Error.TooLong";
    public const string ShortcutErrorDuplicate = "Shortcut.Error.Duplicate";
    public const string ShortcutTab = "Shortcut.Tab";
    public const string ShortcutEmpty = "Shortcut.Empty";
    public const string ShortcutMenuRename = "Shortcut.Menu.Rename";
    public const string ShortcutMenuMoveFirst = "Shortcut.Menu.MoveFirst";
    public const string ShortcutMenuMoveForward = "Shortcut.Menu.MoveForward";
    public const string ShortcutMenuDelete = "Shortcut.Menu.Delete";
    public const string ShortcutAddButton = "Shortcut.AddButton";
    public const string ShortcutAddButtonTip = "Shortcut.AddButton.Tip";
    public const string ShortcutFull = "Shortcut.Full";

    public const string ShortcutErrorStorage = "Shortcut.Error.Storage";

    public const string SendHistoryClear = "Send.HistoryClear";
    public const string SendHistoryClearTitle = "Send.HistoryClear.Title";
    public const string SendHistoryClearMessage = "Send.HistoryClear.Message";
    public const string SendHistoryClearConfirm = "Send.HistoryClear.Confirm";
    public const string SendHistoryClearFailed = "Send.HistoryClear.Failed";

    public const string CloseWhileRecordingTitle = "Session.CloseWhileRecording.Title";
    public const string CloseWhileRecordingMessage = "Session.CloseWhileRecording.Message";
    public const string CloseWhileRecordingConfirm = "Session.CloseWhileRecording.Confirm";

    public const string InjectionTitle = "Injection.Title";
    public const string InjectionMessage = "Injection.Message";
    public const string InjectionConfirm = "Injection.Confirm";

    public const string AboutTitle = "About.Title";
    public const string AboutMessage = "About.Message";

    public const string ChannelPortWithAlias = "Channel.PortWithAlias";

    public const string ChannelAliasPlaceholder = "Channel.AliasPlaceholder";

    public const string ErrorUnsupportedSessionKind = "Error.UnsupportedSessionKind";

    public const string ErrorNoticeTitle = "ErrorNotice.Title";
    public const string ErrorNoticeDismiss = "ErrorNotice.Dismiss";

    public const string StorageNotice = "Storage.Notice";

    public const string ErrorKindUnknown = "ErrorKind.Unknown";
    public const string ErrorKindPortNotFound = "ErrorKind.PortNotFound";
    public const string ErrorKindAccessDenied = "ErrorKind.AccessDenied";
    public const string ErrorKindDeviceRemoved = "ErrorKind.DeviceRemoved";
    public const string ErrorKindTimeout = "ErrorKind.Timeout";
    public const string ErrorKindNotConnected = "ErrorKind.NotConnected";
    public const string ErrorKindInvalidInput = "ErrorKind.InvalidInput";

    public const string ErrorKindIntervalTooSmall = "ErrorKind.IntervalTooSmall";

    public const string ErrorKindNothingToSend = "ErrorKind.NothingToSend";

    public const string ErrorKindRecordingFailed = "ErrorKind.RecordingFailed";
    public const string ErrorKindLineError = "ErrorKind.LineError";

    public const string DialogOpenFailedTerminal = "Dialog.NewSession.OpenFailed.Terminal";
    public const string DialogOpenFailedMonitor = "Dialog.NewSession.OpenFailed.Monitor";

    public const string DialogNewSessionTerminal = "Dialog.NewSession.Terminal";
    public const string DialogNewSessionTerminalDesc = "Dialog.NewSession.TerminalDesc";
    public const string DialogNewSessionMonitor = "Dialog.NewSession.Monitor";
    public const string DialogNewSessionMonitorDesc = "Dialog.NewSession.MonitorDesc";
    public const string DialogNewSessionMonitorRequires = "Dialog.NewSession.MonitorRequires";

    public const string SignalStateHigh = "Signal.StateHigh";
    public const string SignalStateLow = "Signal.StateLow";
    public const string SignalStateUnknown = "Signal.StateUnknown";

    public const string RecordingsQuery = "Dialog.Recordings.Query";
    public const string RecordingsStateEmpty = "Dialog.Recordings.State.Empty";
    public const string RecordingsStateUnfinished = "Dialog.Recordings.State.Unfinished";
    public const string RecordingsStateRecording = "Dialog.Recordings.State.Recording";
    public const string RecordingsCompact = "Dialog.Recordings.Compact";
    public const string RecordingsCompacted = "Dialog.Recordings.Compacted";

    public const string RecordingsDatabaseSize = "Dialog.Recordings.DatabaseSize";
    public const string RecordingsSelectSeveral = "Dialog.Recordings.SelectSeveral";
    public const string RecordingsCompactBusy = "Dialog.Recordings.CompactBusy";
    public const string RecordingsTitle = "Dialog.Recordings.Title";
    public const string RecordingsLoadFailed = "Dialog.Recordings.LoadFailed";
    public const string FrameDataTitle = "Dialog.FrameData.Title";
    public const string RecordingsDeleteTitle = "Dialog.Recordings.DeleteTitle";
    public const string RecordingsDeleteMessage = "Dialog.Recordings.DeleteMessage";

    public const string MenuToolsOptions = "Menu.Tools.Options";
    public const string OptionsTitle = "Dialog.Options.Title";
    public const string OptionsGroupConnection = "Dialog.Options.Group.Connection";
    public const string OptionsAutoReconnect = "Dialog.Options.AutoReconnect";
    public const string OptionsAttempts = "Dialog.Options.Attempts";
    public const string OptionsDelay = "Dialog.Options.Delay";
    public const string OptionsReconnectNote = "Dialog.Options.ReconnectNote";
    public const string OptionsGroupDisplay = "Dialog.Options.Group.Display";
    public const string OptionsMaxFrames = "Dialog.Options.MaxFrames";

    public const string OptionsGroupReception = "Dialog.Options.Group.Reception";
    public const string OptionsIdleGapAuto = "Dialog.Options.IdleGapAuto";
    public const string OptionsIdleGap = "Dialog.Options.IdleGap";
    public const string OptionsIdleGapNote = "Dialog.Options.IdleGapNote";
    public const string StateReconnecting = "State.Reconnecting";
    public const string StateReconnectFailed = "State.ReconnectFailed";
    public const string RecordingsDelete = "Dialog.Recordings.Delete";
    public const string RecordingsPortPair = "Dialog.Recordings.PortPair";

    public const string AutoAnswerTab = "AutoAnswer.Tab";
    public const string AutoAnswerEmpty = "AutoAnswer.Empty";
    public const string AutoAnswerAdd = "AutoAnswer.Add";
    public const string AutoAnswerColumnEnabled = "AutoAnswer.Column.Enabled";
    public const string AutoAnswerColumnName = "AutoAnswer.Column.Name";
    public const string AutoAnswerColumnFormat = "AutoAnswer.Column.Format";
    public const string AutoAnswerColumnTrigger = "AutoAnswer.Column.Trigger";
    public const string AutoAnswerColumnResponse = "AutoAnswer.Column.Response";
    public const string AutoAnswerColumnTerminator = "AutoAnswer.Column.Terminator";
    public const string AutoAnswerColumnCount = "AutoAnswer.Column.Count";
    public const string AutoAnswerMenuEdit = "AutoAnswer.Menu.Edit";
    public const string AutoAnswerMenuDelete = "AutoAnswer.Menu.Delete";
    public const string AutoAnswerMenuMoveUp = "AutoAnswer.Menu.MoveUp";
    public const string AutoAnswerMenuMoveDown = "AutoAnswer.Menu.MoveDown";
    public const string AutoAnswerMenuMoveFirst = "AutoAnswer.Menu.MoveFirst";
    public const string AutoAnswerMenuClearCount = "AutoAnswer.Menu.ClearCount";
    public const string AutoAnswerMenuDisableAll = "AutoAnswer.Menu.DisableAll";

    public const string AutoAnswerEnableAll = "AutoAnswer.EnableAll";

    public const string AutoAnswerHelp = "AutoAnswer.Help";

    public const string AutoAnswerHelpMatching = "AutoAnswer.Help.Matching";

    public const string AutoAnswerHelpEnabling = "AutoAnswer.Help.Enabling";
    public const string AutoAnswerDialogTitleAdd = "AutoAnswer.Dialog.TitleAdd";
    public const string AutoAnswerDialogTitleEdit = "AutoAnswer.Dialog.TitleEdit";
    public const string AutoAnswerDialogName = "AutoAnswer.Dialog.Name";
    public const string AutoAnswerDialogTrigger = "AutoAnswer.Dialog.Trigger";
    public const string AutoAnswerDialogResponse = "AutoAnswer.Dialog.Response";
    public const string AutoAnswerDialogTerminator = "AutoAnswer.Dialog.Terminator";
    public const string AutoAnswerErrorFull = "AutoAnswer.Error.Full";
    public const string AutoAnswerErrorName = "AutoAnswer.Error.Name";
    public const string AutoAnswerErrorTrigger = "AutoAnswer.Error.Trigger";
    public const string AutoAnswerErrorResponse = "AutoAnswer.Error.Response";
}
