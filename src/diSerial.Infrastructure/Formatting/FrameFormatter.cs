using System.Globalization;
using System.Text;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Formatting;

/// <summary>
/// IFrameFormatter 的默认实现。
/// 属纯显示逻辑（不涉及串口操作），因此在 V1.0 即为完整实现，
/// 使显示区、日志、导出三处共用同一套格式化规则。
///
/// ⚠️ 全部数值与时间一律使用 <see cref="CultureInfo.InvariantCulture"/>，
/// 不跟随界面语言。原因：若跟随区域，德语等地区会把 "4.1" 输出为 "4,1"，
/// 既降低日志可读性，更会直接破坏 CSV 导出的分隔结构。
/// 工程工具的数据格式必须与界面语言解耦 —— 德国工程师要的是德语菜单，
/// 不是德语小数点。
/// </summary>
public sealed class FrameFormatter(ILocalizedTextResolver textResolver) : IFrameFormatter
{
    // ⚠️ The 16 that used to live here as a const is now the DEFAULT on FormatData, not a fact
    // about the format (P2-100, 2026-08-10). The display works it out from the available width;
    // callers that do not care about width -- export, logs -- take the default and get the
    // classic hexdump they always got.

    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    /// <summary>
    /// 解码摘要的落地文本。
    ///
    /// 本类身处 Infrastructure，不知道界面语言 —— 解析交给注入进来的
    /// <see cref="ILocalizedTextResolver"/>（由 App 层实现）。
    /// 这样日志与导出仍能输出可读文本，而分层不被打破。
    /// </summary>
    public string? FormatDecodedSummary(SerialFrame frame) =>
        textResolver.Resolve(frame.DecodedSummary);

    public string FormatData(ReadOnlyMemory<byte> data, DisplayFormat format, int bytesPerLine = 16)
        => format switch
        {
            DisplayFormat.Ascii => FormatAscii(data.Span),
            DisplayFormat.Hex => FormatHex(data.Span),
            DisplayFormat.HexAndAscii => FormatHexAndAscii(data.Span, ClampBytesPerLine(bytesPerLine)),
            _ => FormatHex(data.Span)
        };

    /// <summary>
    /// ⛔ <b>夹紧，因为 0 或负数会让下面那个 <c>for</c> 永远不前进</b> —— 界面把宽度算成 0
    /// （窗口最小化、布局还没跑过一轮）时真的会传进来，而那时死的是整个 UI 线程。
    /// 上限只是防呆：单行几百字节没人读得了，也不会有窗口那么宽。
    /// </summary>
    private static int ClampBytesPerLine(int requested) => Math.Clamp(requested, 1, 256);

    /// <summary>
    /// ⚠️ <b>绝对时间恒按<u>本地时区</u>渲染</b>（P1-38，2026-07-31 定）。
    ///
    /// <b>为什么必须在这里转</b>：帧的绝对时间有两个来源，偏移量不一样 ——
    /// 内存里的帧带**本地**偏移，而从记录库读回的帧带 <c>+00:00</c>
    /// （存的是 <c>timestamp_utc</c>，<see cref="DateTimeOffset"/> 的偏移在 round-trip 里丢了）。
    /// 直接 <c>ToString</c> 会**按各自的偏移**渲染，于是同一帧在两条导出路径上差一个时区：
    /// 实测「导出」按钮给 <c>16:29:55</c>、「停止记录」给 <c>08:31:27</c>。
    ///
    /// <b>放在这个唯一的渲染出口，一致性就是结构性的</b> ——
    /// 只要还是同一个 formatter，两条路径不可能再分叉；
    /// 而在 reader 里转只修了当下那一条路，将来多一个读回入口就要再记得改一次。
    ///
    /// ⚠️ <b>存储侧一个字都不改</b>：<c>timestamp_utc</c> 是带 <c>Z</c> 的 ISO-8601，
    /// 字典序即时间序，索引 <c>ix_frame_batch_time</c> 依赖这一点。
    /// 改成存本地时间会把索引的正确性一起毁掉。
    ///
    /// ⚠️ <b>已知局限</b>：跨时区打开一个别处录的批次，显示的是<b>你的</b>本地时间，
    /// 不是「当时当地几点」。要还原后者得额外存偏移量，已记为 V1.1 候选。
    /// </summary>
    public string FormatTimestamp(SerialFrame frame, TimestampMode mode) => mode switch
    {
        TimestampMode.None => string.Empty,
        TimestampMode.Absolute => frame.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", Fmt),
        TimestampMode.Relative => FormatElapsed(frame.Elapsed),

        // ⛔ There used to be a TimestampMode.Delta branch here, and it read
        //    frame.Delta is { } d ? DurationText.Milliseconds(d) : "—"
        // which is character-for-character what FrameViewModel.DeltaText still does for the
        // Δms column. Two columns, one expression: the screen showed the same number twice and
        // so did the exported file. Removed with the enum value on 2026-08-06 -- see the
        // comment on TimestampMode for why this side went rather than the Δms column.
        _ => string.Empty
    };

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>Resolved on every render, not cached</b> -- that is what makes a language switch
    /// reach rows already on screen, since <c>LogPanel.RefreshAll</c> calls straight back
    /// through here.
    /// </remarks>
    public string FormatChannelText(SerialFrame frame, string? channelLabel)
    {
        // Blank label = terminal session (Channel is always None there). ⚠️ The test is the
        // LABEL, not the Channel: a monitor session's label always has at least a port name,
        // so it is never blank.
        if (string.IsNullOrWhiteSpace(channelLabel))
        {
            return Resolve(frame.Direction == FrameDirection.Rx
                ? FrameTextKeys.DirectionRx
                : FrameTextKeys.DirectionTx);
        }

        return Resolve(
            frame.Direction == FrameDirection.Tx
                ? FrameTextKeys.ChannelInjected   // what we injected onto the bus
                : FrameTextKeys.ChannelObserved,  // what we observed on it
            channelLabel);
    }

    /// <summary>
    /// Resolves a key through the same seam as the decoded summary.
    ///
    /// <para>⚠️ Falls back to the key itself rather than to an empty string: a missing key must
    /// show up as a visible <c>Frame.Direction.Rx</c> in the channel column, not as a blank one.
    /// A blank column looks like "this frame has no direction", which is a lie about the data.</para>
    /// </summary>
    private string Resolve(string key, params object?[] args) =>
        textResolver.Resolve(LocalizableText.FromKey(key, args)) ?? key;

    private static string FormatElapsed(TimeSpan elapsed) => string.Format(
        Fmt, "{0:D2}:{1:D2}.{2:D3}",
        (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds);

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

    private static string FormatAscii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            sb.Append(IsPrintable(b) ? (char)b : '.');
        }
        return sb.ToString();
    }

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

    private static bool IsPrintable(byte b) => b >= 0x20 && b < 0x7F;
}
