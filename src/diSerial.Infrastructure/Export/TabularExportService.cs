using System.Globalization;
using System.Text;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Export;

/// <summary>
/// 表格式导出（规格 docs/01-spec.md 6.4）。
///
/// <b>三条定死的规则</b>：
///   1. <b>按格式分隔</b> —— <c>.tsv</c> 制表符，<c>.csv</c> 逗号 + RFC 4180 引号转义
///   2. <b>有表头</b>
///   3. <b>一帧一行</b> —— HEX <b>不换行</b>
///
/// <para>⚠️ <b>第 3 条是本类存在的主要理由。</b>
/// <see cref="IFrameFormatter.FormatData"/> 在 HexAndAscii 下每 16 字节换行，
/// 而时间戳只在首行 —— 那样一个 64 字节的帧在文件里是 4 个物理行、只有第一行有时间戳，
/// <b>表头会对不上，<c>grep</c> 与按行解析全部失效</b>。</para>
///
/// <para>⛔⭐⭐ <b>而「折平换行」曾经是<u>不够的</u>（P2-101，2026-08-11 修）。</b>
/// 本类原先把 <c>FormatData(HexAndAscii)</c> 的结果整个折平塞进一个 <c>Data</c> 格，
/// 于是 64 字节的帧变成 <c>hex×16 ascii×16 hex×16 ascii×16 …</c> ——
/// <b>一格里两种东西，交替出现。</b>
/// ⚠️ <b>而它连原理上都分不开</b>：载荷是 <c>0x31</c> 时 ASCII 渲染成 <c>'1'</c>，
/// <b>那也是一个十六进制数字字符</b>，解析器找不到 hex 到哪里结束 ——
/// 而数字字符恰恰是最常见的测试载荷。
/// ⭐ <b>教训</b>：换行只是显示产物<u>之一</u>。把「给人看的渲染」搬进「给机器读的文件」时，
/// 要问的是<b>「这一格里有几种东西」</b>，不是「有没有换行」。</para>
///
/// <para>⭐ <b>所以数据现在是<u>两列</u></b>：<c>Data</c> 恒为 hex（无损、且字符集只有
/// <c>[0-9A-F ]</c>），<c>DataAscii</c> 是可读渲染。⛔ <b><c>Data</c> 恒为 hex 是不可协商的</b> ——
/// 一个内容类型随设置变化的列，正是上面那个缺陷的根。</para>
///
/// <para>⚠️ <b>表头是固定英文，刻意不本地化。</b>
/// 两个理由：其一，本类在 Infrastructure，
/// <see href="https://example.invalid">03-conventions 2.3</see> 规定下层不得产出用户可见文本；
/// 其二更实在 —— 表头若跟界面语言走，<b>同一份导出在中英文下列名不同，
/// 任何解析脚本都会因为用户切了语言而失效</b>。机器可读的文件需要稳定的列名。</para>
/// </summary>
public sealed class TabularExportService(IFrameFormatter formatter) : IExportService
{
    private const char TabSeparator = '\t';
    private const char CommaSeparator = ',';

    /// <summary>
    /// ⛔ <b>RFC 4180：含分隔符、引号、CR 或 LF 的字段必须加引号，内部引号写成两个。</b>
    ///
    /// <para>⚠️ <b>这不是防御性编程，是<u>必须</u></b>：<c>DataAscii</c> 列直接来自
    /// <c>FormatAscii</c>，而它对 <c>0x20</c>–<c>0x7E</c> 原样输出 ——
    /// <b>逗号 <c>0x2C</c> 与双引号 <c>0x22</c> 都在这个区间里</b>，
    /// 串口上收到一个逗号就会多切出一列。<c>Alias</c> 是用户自己敲的，同理。</para>
    /// </summary>
    private static readonly char[] CsvSpecials = [CommaSeparator, '"', '\r', '\n'];

    /// <summary>数值恒用 invariant —— 德语区不得把 4.1 写成 4,1（03-conventions 第三节）。</summary>
    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    public IReadOnlyList<ExportFormat> SupportedFormats { get; } =
        [ExportFormat.Tsv, ExportFormat.Csv];

    private static char SeparatorFor(ExportFormat format) =>
        format == ExportFormat.Csv ? CommaSeparator : TabSeparator;

    /// <summary>
    /// 一个字段落到文件里的样子。⭐ <b>两种格式走的是<u>不同</u>的路，别合并</b>：
    /// tsv 没有转义机制，只能把分隔符<b>换掉</b>（信息有损，而列不会错位）；
    /// csv 有转义机制，所以<b>原样保留</b>再加引号（无损）。
    /// </summary>
    private static string Encode(string text, ExportFormat format) => format == ExportFormat.Csv
        ? (text.IndexOfAny(CsvSpecials) >= 0 ? '"' + text.Replace("\"", "\"\"") + '"' : text)
        : Flatten(text);

    public async Task ExportAsync(
        IEnumerable<SerialFrame> frames,
        string filePath,
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // UTF-8 **不带 BOM**：带 BOM 的话第一个列名前面会多出三个字节，
        // 严格的解析器会把 "Seq" 读成 "﻿Seq"。踩过一次（那次是 clip.exe 加的）。
        await using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(false));

        // ⛔ CRLF on every platform (user decision, 2026-08-15). WriteLineAsync defaults to
        // Environment.NewLine, which made the same session export as CRLF on Windows and LF on
        // macOS -- two byte-different files from one product, for data users compare across
        // machines. CRLF is the direction that leaves the released Windows 1.0 output untouched;
        // picking LF would have changed bytes users already have.
        //
        // ⚠️ This is the ONLY writer that produces an export, so it is the only place that has
        // to say it. If a second export path ever appears, it has to set this too -- pinned by
        // ExportLineEndingTests.
        writer.NewLine = "\r\n";

        await writer.WriteLineAsync(BuildHeader(options));

        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(BuildRow(frame, options));
        }
    }

    private static string BuildHeader(ExportOptions options)
    {
        var columns = new List<string> { "Seq", "Time" };
        if (options.IncludeDeltaColumn) columns.Add("DeltaMs");
        // ⚠️ 2026-08-01：Channel（A/B）列改为 Port（端口名）—— 见 ExportOptions.ChannelPortA。
        if (options.IncludeChannelColumn) { columns.Add("Port"); columns.Add("Alias"); }
        columns.Add("Direction");

        // ⭐ 两列，顺序有意义：Data（hex，无损）在前，DataAscii（可读）在后。
        // ⚠️ DataAscii 是 2026-08-11 **加在末尾**的（P2-101）—— 加在末尾而不是插在中间，
        // 是为了让按下标读列的既有脚本继续能用：它们读到的每一列位置都没变。
        columns.Add("Data");
        columns.Add("DataAscii");
        return string.Join(SeparatorFor(options.Format), columns);
    }

    /// <summary>
    /// 通道对应的端口名，没有就返回空串（→ 空单元格）。
    /// 空是解析器认得的「无值」，而占位符会变成一个要特判的字符串 ——
    /// 与 <c>DeltaMs</c> 首帧留空是同一条理。
    /// </summary>
    private static string PortOf(ChannelId channel, ExportOptions options) => channel switch
    {
        ChannelId.A => options.ChannelPortA ?? string.Empty,
        ChannelId.B => options.ChannelPortB ?? string.Empty,
        _ => string.Empty
    };

    /// <inheritdoc cref="PortOf"/>
    private static string AliasOf(ChannelId channel, ExportOptions options) => channel switch
    {
        ChannelId.A => options.ChannelAliasA ?? string.Empty,
        ChannelId.B => options.ChannelAliasB ?? string.Empty,
        _ => string.Empty
    };

    private string BuildRow(SerialFrame frame, ExportOptions options)
    {
        var cells = new List<string>
        {
            frame.Sequence.ToString(Fmt),
            formatter.FormatTimestamp(frame, options.TimestampMode)
        };

        if (options.IncludeDeltaColumn)
        {
            // 首帧没有上一帧可比。界面上显示 '–'，文件里留**空单元格** ——
            // 空是解析器认得的「无值」，而 '–' 会变成一个要特判的字符串。
            cells.Add(frame.Delta.HasValue
                ? frame.Delta.Value.TotalMilliseconds.ToString("F1", Fmt)
                : string.Empty);
        }

        if (options.IncludeChannelColumn)
        {
            // Port 是端口名（唯一且稳定，且自解释），别名单独一列 —— 见 ExportOptions 的注释。
            // ⚠️ 2026-08-01 前这里写的是 frame.Channel.ToString()，即 A / B。
            cells.Add(Encode(PortOf(frame.Channel, options), options.Format));
            cells.Add(Encode(AliasOf(frame.Channel, options), options.Format));
        }

        // ⭐ 方向列恒输出。监听会话里**自己注入的帧与总线上观测到的帧**靠它区分 ——
        // 界面上目前分不出来（P1-32），导出文件里不该也分不出来。
        cells.Add(frame.Direction.ToString());

        // ⛔⭐ Data 恒为 hex，**不看 options.DisplayFormat**（P2-101）。
        // 它是无损的那一列，而一个内容类型随设置变化的列正是本类注释里说的那个缺陷的根。
        cells.Add(Encode(formatter.FormatData(frame.Data, DisplayFormat.Hex), options.Format));

        // ⭐ DataAscii **跟随** DisplayFormat：用户明说只要 HEX 时留空单元格。
        // 空是解析器认得的「无值」—— 与 Alias 没设、首帧没有 Delta 同一条理。
        cells.Add(Encode(
            options.DisplayFormat == DisplayFormat.Hex
                ? string.Empty
                : formatter.FormatData(frame.Data, DisplayFormat.Ascii),
            options.Format));

        return string.Join(SeparatorFor(options.Format), cells);
    }

    /// <summary>
    /// 把渲染结果折成单行：换行换成空格，制表符换成空格（否则会多切出一列）。
    /// ⚠️ <b>tsv 专用</b> —— csv 走引号转义，见 <see cref="Encode"/>。
    /// </summary>
    private static string Flatten(string text) => text
        .Replace("\r\n", " ")
        .Replace('\n', ' ')
        .Replace('\r', ' ')
        .Replace(TabSeparator, ' ');
}
