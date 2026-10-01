using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Sessions;

// Group shared session services.
public sealed record SessionContext(
    IFrameFormatter Formatter,
    ILocalizationService Localization,
    IEnumChoiceProvider EnumChoices,
    IExportService ExportService,
    IRecordingReader RecordingReader,
    IDialogService DialogService,
    IAppSettings Settings,
    ILoggerFactory LoggerFactory,
    IPeriodicTimerFactory Timers,
    ISendHistoryStore SendHistory,
    ISendHistoryScheduler SendHistoryScheduler,

    SessionLogBuffer? SessionLog = null,

    IShortcutStore? Shortcuts = null,

    IAutoAnswerStore? AutoAnswer = null);
