using System.Globalization;
using System.Text;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Formatting;

// Format captured frames.
public sealed class FrameFormatter(ILocalizedTextResolver textResolver) : IFrameFormatter
{

    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    // Format decoded frame content.
    public string? FormatDecodedSummary(SerialFrame frame) =>
        textResolver.Resolve(frame.DecodedSummary);

    // Format frame payload bytes.
    public string FormatData(ReadOnlyMemory<byte> data, DisplayFormat format, int bytesPerLine = 16)
        => format switch
        {
            DisplayFormat.Ascii => FormatAscii(data.Span),
            DisplayFormat.Hex => FormatHex(data.Span),
            DisplayFormat.HexAndAscii => FormatHexAndAscii(data.Span, ClampBytesPerLine(bytesPerLine)),
            _ => FormatHex(data.Span)
        };

    // Clamp the frame line width.
    private static int ClampBytesPerLine(int requested) => Math.Clamp(requested, 1, 256);

    // Format a frame timestamp.
    public string FormatTimestamp(SerialFrame frame, TimestampMode mode) => mode switch
    {
        TimestampMode.None => string.Empty,
        TimestampMode.Absolute => frame.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", Fmt),
        TimestampMode.Relative => FormatElapsed(frame.Elapsed),

        _ => string.Empty
    };

    // Format the frame channel label.
    public string FormatChannelText(SerialFrame frame, string? channelLabel)
    {
        if (string.IsNullOrWhiteSpace(channelLabel))
        {
            if (frame.Direction == FrameDirection.Tx && frame.IsAutoAnswer)
            {
                return Resolve(FrameTextKeys.DirectionAuto);
            }

            return Resolve(frame.Direction == FrameDirection.Rx
                ? FrameTextKeys.DirectionRx
                : FrameTextKeys.DirectionTx);
        }

        return Resolve(
            frame.Direction == FrameDirection.Tx
                ? FrameTextKeys.ChannelInjected
                : FrameTextKeys.ChannelObserved,
            channelLabel);
    }

    // Resolve translatable frame text.
    private string Resolve(string key, params object?[] args) =>
        textResolver.Resolve(LocalizableText.FromKey(key, args)) ?? key;

    // Format elapsed monotonic time.
    private static string FormatElapsed(TimeSpan elapsed) => string.Format(
        Fmt, "{0:D2}:{1:D2}.{2:D3}",
        (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds);

    // Format hexadecimal payload text.
    private static string FormatHex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2", Fmt));
        }
        return sb.ToString();
    }

    // Format printable payload text.
    private static string FormatAscii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            sb.Append(IsPrintable(b) ? (char)b : '.');
        }
        return sb.ToString();
    }

    // Format hex and ASCII columns.
    private static string FormatHexAndAscii(ReadOnlySpan<byte> data, int bytesPerLine)
    {
        if (data.IsEmpty) return string.Empty;

        var sb = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            if (offset > 0) sb.AppendLine();

            var chunk = data.Slice(offset, Math.Min(bytesPerLine, data.Length - offset));

            for (var i = 0; i < bytesPerLine; i++)
            {
                sb.Append(i < chunk.Length ? chunk[i].ToString("X2", Fmt) : "  ").Append(' ');
            }

            sb.Append(' ');
            foreach (var b in chunk)
            {
                sb.Append(IsPrintable(b) ? (char)b : '.');
            }
        }
        return sb.ToString();
    }

    // Check for printable ASCII bytes.
    private static bool IsPrintable(byte b) => b >= 0x20 && b < 0x7F;
}
