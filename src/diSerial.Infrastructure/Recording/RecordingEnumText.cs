using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Parse stored recording values.
internal static class RecordingEnumText
{
    private static readonly FrameFlags KnownFlags =
        FrameFlags.ParityError | FrameFlags.FramingError |
        FrameFlags.BufferOverrun | FrameFlags.ProtocolError;

    // Parse a stored parity setting.
    public static SerialParity ParseParity(string stored, ILogger logger, long batchId)
    {
        if (Enum.TryParse<SerialParity>(stored, out var value)) return value;

        logger.LogWarning(
            "Batch {BatchId} has an unrecognised parity {Stored}; reporting it as {Fallback}, " +
            "so the settings shown for this batch are not what was recorded.",
            batchId, stored, SerialParity.None);

        return SerialParity.None;
    }

    // Parse a stored stop bit setting.
    public static SerialStopBits ParseStopBits(string stored, ILogger logger, long batchId)
    {
        if (Enum.TryParse<SerialStopBits>(stored, out var value)) return value;

        logger.LogWarning(
            "Batch {BatchId} has an unrecognised stop_bits {Stored}; reporting it as {Fallback}, " +
            "so the settings shown for this batch are not what was recorded.",
            batchId, stored, SerialStopBits.One);

        return SerialStopBits.One;
    }

    // Parse a stored channel ID.
    public static bool TryChannel(long stored, out ChannelId value)
    {
        if (Enum.IsDefined(typeof(ChannelId), (int)stored))
        {
            value = (ChannelId)stored;
            return true;
        }

        value = ChannelId.None;
        return false;
    }

    // Parse a stored frame direction.
    public static bool TryDirection(long stored, out FrameDirection value)
    {
        if (Enum.IsDefined(typeof(FrameDirection), (int)stored))
        {
            value = (FrameDirection)stored;
            return true;
        }

        value = FrameDirection.Rx;
        return false;
    }

    // Parse stored frame flags.
    public static bool TryFlags(long stored, out FrameFlags value)
    {
        var candidate = (FrameFlags)stored;

        if ((candidate & ~KnownFlags) == 0)
        {
            value = candidate;
            return true;
        }

        value = FrameFlags.None;
        return false;
    }
}
