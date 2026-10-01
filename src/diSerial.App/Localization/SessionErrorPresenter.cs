using DiSerial.Core.Models;

namespace DiSerial.App.Localization;

// Localize serial errors.
public static class SessionErrorPresenter
{
    // Localize serial errors.
    public static string Describe(SerialErrorKind kind, ILocalizationService localization)
        => localization[KeyOf(kind)];

    // Find the error translation key.
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

        _ => LocKeys.ErrorKindUnknown
    };
}
