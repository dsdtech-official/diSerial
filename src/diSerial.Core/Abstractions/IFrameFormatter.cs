using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 帧数据的文本化（C-05、C-06）。
///
/// 单独抽出的原因：显示区、日志文件、剪贴板复制、导出四处需要一致的格式化
/// 规则，集中在此避免重复实现。
/// </summary>
public interface IFrameFormatter
{
    /// <summary>
    /// 按指定格式渲染帧的数据部分。
    /// </summary>
    /// <param name="bytesPerLine">
    /// ⭐ <b>仅 <c>HexAndAscii</c> 用得上</b>：一行放几个字节（P2-100，2026-08-10）。
    ///
    /// <para>⚠️ <b>它是<u>显示</u>的事，不是格式的事</b> —— 显示区把它按当前可用宽度算出来传进来，
    /// 窗口越宽一行越多字节。⛔ <b>而默认值 16 必须留着</b>：导出与其它调用方不关心宽度，
    /// 它们拿到的换行[随后会被折平](../../diSerial.Infrastructure/Export/TabularExportService.cs)。</para>
    ///
    /// <para>⛔ <b>实现必须自己夹紧</b>：调用方传 0 或负数时不许除零或死循环。</para>
    /// </param>
    string FormatData(ReadOnlyMemory<byte> data, DisplayFormat format, int bytesPerLine = 16);

    /// <summary>按指定模式渲染时间戳列。</summary>
    string FormatTimestamp(SerialFrame frame, TimestampMode mode);

    /// <summary>
    /// 渲染解码摘要。无解码结果时返回 null。
    ///
    /// 摘要在帧上是 <see cref="LocalizableText"/>（只有键与参数），
    /// 由实现方借助 <see cref="ILocalizedTextResolver"/> 落地成当前语言的文本。
    /// 显示层因此不必自己持有解析器。
    /// </summary>
    string? FormatDecodedSummary(SerialFrame frame);

    /// <summary>
    /// Renders the channel/direction column (P2-69).
    ///
    /// <para><paramref name="channelLabel"/> is the monitor session's per-channel label
    /// (<c>COM6</c> or <c>COM6 · PLC</c>), already assembled by the display layer. Null or blank
    /// means a terminal session, which has no channels and shows a bare direction marker.</para>
    ///
    /// <para><b>Why this lives on the formatter rather than in the row ViewModel.</b> The text
    /// comes from resources, and the row ViewModel is created once per frame (500 of them in
    /// the display buffer), so it deliberately holds no <c>ILocalizationService</c> and does not
    /// subscribe to <c>CultureChanged</c>. The formatter already carries an
    /// <see cref="ILocalizedTextResolver"/> for <see cref="FormatDecodedSummary"/>, and the row
    /// is already re-rendered through this same object on a language change
    /// (<c>SessionViewModel.OnCultureChanged</c> -> <c>LogPanel.RefreshAll</c>). Reusing that
    /// seam costs zero new subscriptions.</para>
    ///
    /// <para>⚠️ <b>Both languages currently hold the same values</b> (<c>TX</c> / <c>RX</c> are
    /// the universal serial abbreviations). That is not a reason to inline them again: the same
    /// shape with <c>ASCII</c> / <c>HEX</c> sat hardcoded in the send panel for months and
    /// produced a Chinese UI reading 「十六进制」 in a dropdown and <c>HEX</c> on the button
    /// beside it. Being in resources is what makes translating it later a data change.</para>
    /// </summary>
    string FormatChannelText(SerialFrame frame, string? channelLabel);
}
