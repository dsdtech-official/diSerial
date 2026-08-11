using DiSerial.Core.Models;

namespace DiSerial.App.Localization;

/// <summary>
/// 把 Core 层的平台支持状态码映射为当前语言的提示文本。
///
/// 这是「下层返回错误码、App 层负责本地化」这一分层约定的落点：
/// <c>ISerialPortFactory</c> 只返回 <see cref="PlatformSupportStatus"/>，
/// 不产出任何用户可见字符串，从而保持 Core / Infrastructure 与界面语言无关、
/// 且可脱离 UI 单元测试。
/// </summary>
public static class PlatformStatusPresenter
{
    /// <summary>返回需要向用户展示的警告文本；平台正常时返回 null。</summary>
    public static string? Describe(PlatformSupportStatus status, ILocalizationService localization)
        => status switch
        {
            PlatformSupportStatus.Supported => null,
            PlatformSupportStatus.MacOsNotImplemented =>
                localization[LocKeys.PlatformMacOsNotImplemented],
            _ => localization[LocKeys.PlatformUnknown]
        };
}
