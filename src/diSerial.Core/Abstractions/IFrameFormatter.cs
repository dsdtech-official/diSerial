using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IFrameFormatter
{
    // Format frame payload bytes.
    string FormatData(ReadOnlyMemory<byte> data, DisplayFormat format, int bytesPerLine = 16);

    // Format a frame timestamp.
    string FormatTimestamp(SerialFrame frame, TimestampMode mode);

    // Format decoded frame content.
    string? FormatDecodedSummary(SerialFrame frame);

    // Format the frame channel label.
    string FormatChannelText(SerialFrame frame, string? channelLabel);
}
