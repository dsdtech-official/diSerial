using DiSerial.Core.Models;

namespace DiSerial.App.Localization;

// Localize platform status.
public static class PlatformStatusPresenter
{
    // Localize platform support.
    public static string? Describe(PlatformSupportStatus status, ILocalizationService localization)
        => status switch
        {
            PlatformSupportStatus.Supported => null,
            PlatformSupportStatus.MacOsNotImplemented =>
                localization[LocKeys.PlatformMacOsNotImplemented],
            _ => localization[LocKeys.PlatformUnknown]
        };
}
