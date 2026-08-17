using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 把一个记录批次读回成 <see cref="SerialFrame"/>，供导出使用（C-09 的导出那一步）。
///
/// ⚠️ <b>读出来的是原始字节</b> —— 渲染成哪种格式由导出那一步决定。
/// 这正是「只存原始字节」那个决定换来的：
/// <b>记录当时选错了显示格式，不再是不可挽回的错误。</b>
/// </summary>
public interface IRecordingReader
{
    /// <summary>
    /// 按序号升序读回一个批次的全部帧。
    ///
    /// ⚠️ <b>刻意是 <see cref="IAsyncEnumerable{T}"/> 而不是 List</b>：
    /// 一个批次可能有几十万帧，而导出是流式写文件的 ——
    /// 没有理由让整批数据和输出流同时占着内存。
    /// </summary>
    IAsyncEnumerable<SerialFrame> ReadBatchAsync(long batchId, CancellationToken cancellationToken = default);

    /// <summary>批次的元信息，用于拼默认文件名。批次不存在时返回 null。</summary>
    Task<RecordingBatchSummary?> GetBatchAsync(long batchId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch metadata. <b>Deliberately never written into an export file</b> — a leading
/// <c>#</c> comment row is read as a data row by Excel and by strict TSV parsers. Its use is
/// to compose a default file name; the full information stays in the database.
///
/// <para>⚠️ <b>Alias is the single exception to that rule</b> (P0-7 a, decided 2026-07-31):
/// it goes into the export as its own <c>Alias</c> column. The reasoning is on
/// <see cref="ExportOptions.ChannelAliasA"/> — the port number can be recovered from the
/// file name, <b>the alias cannot, and is lost outright if it is not in the file</b>.</para>
/// </summary>
/// <param name="PortLabel">
/// Terminal: <c>COM7</c>. Monitor: <c>COM2-COM4</c>.
///
/// <para>⚠️ Carries the <b>same guarantee as <c>SettingsLabel</c> below</b>: no path
/// separator and no character invalid in a file name, so composing a name from it needs no
/// further escaping. Each port name is passed through
/// <see cref="SerialPortInfo.FileNameSegment"/> before the two are joined.</para>
///
/// <para>⛔ <b>P2-117.</b> This used to be the raw port name. On Windows the difference is
/// invisible (<c>COM7</c> is unchanged); on macOS a port name is a path, so the composed
/// file name carried slashes and the export silently wrote into directories it created.
/// ⭐ Note the guarantee is on the LABEL, not on the file name: the naming rule itself is
/// still the App layer's, exactly as spelled out for <c>SettingsLabel</c>.</para>
/// </param>
/// <param name="SettingsLabel">
/// The port settings in hyphenated short form, e.g. <c>9600-8N1</c> — that is,
/// <see cref="SerialPortSettings.ShortDescription"/> with its space replaced.
///
/// <para>⚠️ <b>"already suitable for a file name" is what this used to claim, and it said too
/// much</b> (P2-34, 2026-08-05): whether a name is suitable is an App-layer decision — legal
/// characters, length limits, the surrounding pattern — and stating it here read as
/// Infrastructure settling a presentation question. What this value actually guarantees is
/// narrower and is all a caller may rely on: <b>it contains no whitespace and no character
/// that is invalid in a path</b>, so composing a name from it needs no further escaping. The
/// name itself is composed by the session view models.</para>
/// </param>
/// <param name="AliasA">Channel A's alias; null for terminal sessions and when unnamed.</param>
/// <param name="AliasB">Channel B's alias; null for terminal sessions and when unnamed.</param>
public sealed record RecordingBatchSummary(
    long Id,
    string PortLabel,
    string SettingsLabel,
    DateTimeOffset StartedAt,
    string? AliasA = null,
    string? AliasB = null);
