using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// 会话 ViewModel 所需的表现层服务集合。
///
/// 打包成一个记录而非逐个注入，是为了避免会话基类与两个派生类的构造函数
/// 随服务增加而不断膨胀 —— 后续新增会话类型时也只需传递这一个参数。
/// </summary>
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
    ISendHistoryStore SendHistory);
