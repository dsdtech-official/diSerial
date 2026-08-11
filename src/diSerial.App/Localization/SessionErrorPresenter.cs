using DiSerial.Core.Models;

namespace DiSerial.App.Localization;

/// <summary>
/// 把 <see cref="SerialErrorKind"/> 映射为当前语言的原因文本。
///
/// 与 <see cref="PlatformStatusPresenter"/> 是同一套路，也是
/// 03-conventions 2.3「下层返回错误码 / 枚举，App 层负责映射」这条分层约定的落点。
///
/// ⚠️ <b>刻意不显示框架异常的原文</b>（如 "Access to the port 'COM3' is denied."）：
/// 那是英文，直接显示会让中文界面半英半中，违反「界面上的字都能切语言」
/// （03-conventions 2.1）。原文照常进日志 —— 排查现场问题靠它，不靠界面。
/// </summary>
public static class SessionErrorPresenter
{
    /// <summary>返回该错误分类对应的用户可读原因。</summary>
    public static string Describe(SerialErrorKind kind, ILocalizationService localization)
        => localization[KeyOf(kind)];

    private static string KeyOf(SerialErrorKind kind) => kind switch
    {
        SerialErrorKind.PortNotFound => LocKeys.ErrorKindPortNotFound,
        SerialErrorKind.AccessDenied => LocKeys.ErrorKindAccessDenied,
        SerialErrorKind.DeviceRemoved => LocKeys.ErrorKindDeviceRemoved,
        SerialErrorKind.Timeout => LocKeys.ErrorKindTimeout,
        SerialErrorKind.NotConnected => LocKeys.ErrorKindNotConnected,
        SerialErrorKind.InvalidInput => LocKeys.ErrorKindInvalidInput,
        SerialErrorKind.IntervalTooSmall => LocKeys.ErrorKindIntervalTooSmall,
        SerialErrorKind.NothingToSend => LocKeys.ErrorKindNothingToSend,
        SerialErrorKind.RecordingFailed => LocKeys.ErrorKindRecordingFailed,
        SerialErrorKind.LineError => LocKeys.ErrorKindLineError,

        // Unknown 与将来新增但尚未配文案的取值都走这里 ——
        // 显示一句通用说明，而不是空白或键名。细节引导用户去看日志。
        _ => LocKeys.ErrorKindUnknown
    };
}
