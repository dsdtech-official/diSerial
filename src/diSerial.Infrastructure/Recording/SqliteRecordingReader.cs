using System.Globalization;
using System.Runtime.CompilerServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;

namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// <see cref="IRecordingReader"/> 的 SQLite 实现。表结构见 <see cref="RecordingSchema"/>。
///
/// ⚠️ <b>每次调用自己开一条连接、用完即关</b>：读是偶发的（只在导出时），
/// 而记录器那边的连接归它自己的写盘任务独占。两边不共享连接可以省掉一整类并发问题，
/// 代价只是导出时多一次 open —— WAL 模式下读写本来就不互斥。
/// </summary>
public sealed class SqliteRecordingReader(IAppPaths paths) : IRecordingReader
{
    public async IAsyncEnumerable<SerialFrame> ReadBatchAsync(
        long batchId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, timestamp_utc, elapsed_ms, delta_ms, channel, direction, flags, data
            FROM frame WHERE batch_id = $b ORDER BY seq;
            """;
        cmd.Parameters.AddWithValue("$b", batchId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new SerialFrame
            {
                Sequence = reader.GetInt64(0),
                Timestamp = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                Elapsed = TimeSpan.FromMilliseconds(reader.GetDouble(2)),
                Delta = reader.IsDBNull(3) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(3)),
                Channel = (ChannelId)reader.GetInt32(4),
                Direction = (FrameDirection)reader.GetInt32(5),
                Flags = (FrameFlags)reader.GetInt32(6),
                Data = (byte[])reader["data"]
            };
        }
    }

    public async Task<RecordingBatchSummary?> GetBatchAsync(
        long batchId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT port_a, port_b, baud_rate, data_bits, parity, stop_bits, started_at,
                   alias_a, alias_b
            FROM batch WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", batchId);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        var portA = r.GetString(0);
        var portLabel = r.IsDBNull(1) ? portA : $"{portA}-{r.GetString(1)}";

        // ⚠️ **复用 SerialPortSettings.ShortDescription，不要另写一套缩写映射。**
        // 初版在这里自己取首字母，于是 StopBits 的 "One" 变成了 'O' ——
        // 默认文件名成了 diserial-COM1-9600-8NO-…（应为 8N1）。
        // 那段映射在 SerialPortSettings 里本来就有、而且有测试守着。
        var settings = new SerialPortSettings
        {
            BaudRate = r.GetInt32(2),
            DataBits = r.GetInt32(3),
            Parity = Enum.TryParse<SerialParity>(r.GetString(4), out var p) ? p : SerialParity.None,
            StopBits = Enum.TryParse<SerialStopBits>(r.GetString(5), out var s) ? s : SerialStopBits.One
        };

        // 形如 9600-8N1。⚠️ 用连字符而不是空格 —— 这个串要进文件名。
        var settingsLabel = settings.ShortDescription.Replace(' ', '-');

        return new RecordingBatchSummary(
            batchId,
            portLabel,
            settingsLabel,
            DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture),
            // 别名进导出文件的 Alias 列（P0-7 a）。批次记录的是**开始记录那一刻**的别名。
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8));
    }
}
