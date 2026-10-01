using System.Globalization;
using System.Runtime.CompilerServices;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Recording;

// Read recorded frame batches.
public sealed class SqliteRecordingReader(IAppPaths paths, ILogger? logger = null)
    : IRecordingReader
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    // Stream recorded batch frames.
    public async IAsyncEnumerable<SerialFrame> ReadBatchAsync(
        long batchId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        await conn.OpenAsync(cancellationToken);
        RecordingDatabase.CheckVersion(conn, paths.RecordingDatabasePath);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, timestamp_utc, elapsed_ms, delta_ms, channel, direction, flags, data
            FROM frame WHERE batch_id = $b ORDER BY seq;
            """;
        cmd.Parameters.AddWithValue("$b", batchId);

        long badChannel = 0, badDirection = 0, badFlags = 0, firstBadSeq = -1;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var seq = reader.GetInt64(0);

            if (!RecordingEnumText.TryChannel(reader.GetInt64(4), out var channel)) badChannel++;
            if (!RecordingEnumText.TryDirection(reader.GetInt64(5), out var direction)) badDirection++;
            if (!RecordingEnumText.TryFlags(reader.GetInt64(6), out var flags)) badFlags++;

            if (firstBadSeq < 0 && (badChannel + badDirection + badFlags) > 0) firstBadSeq = seq;

            yield return new SerialFrame
            {
                Sequence = seq,
                Timestamp = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                Elapsed = TimeSpan.FromMilliseconds(reader.GetDouble(2)),
                Delta = reader.IsDBNull(3) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(3)),
                Channel = channel,
                Direction = direction,
                Flags = flags,
                Data = (byte[])reader["data"]
            };
        }

        if (badChannel + badDirection + badFlags > 0)
        {
            _logger.LogWarning(
                "Batch {BatchId} had frames with values outside the known enums " +
                "(channel {BadChannel}, direction {BadDirection}, flags {BadFlags}; first at seq {FirstSeq}). " +
                "They were read as the neutral member, so the export does not describe them correctly.",
                batchId, badChannel, badDirection, badFlags, firstBadSeq);
        }
    }

    // Read a recorded batch summary.
    public async Task<RecordingBatchSummary?> GetBatchAsync(
        long batchId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        await conn.OpenAsync(cancellationToken);
        RecordingDatabase.CheckVersion(conn, paths.RecordingDatabasePath);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT port_a, port_b, baud_rate, data_bits, parity, stop_bits, started_at,
                   alias_a, alias_b
            FROM batch WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", batchId);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        var portA = SerialPortInfo.FileNameSegment(r.GetString(0));
        var portLabel = r.IsDBNull(1)
            ? portA
            : $"{portA}-{SerialPortInfo.FileNameSegment(r.GetString(1))}";

        var settings = new SerialPortSettings
        {
            BaudRate = r.GetInt32(2),
            DataBits = r.GetInt32(3),
            Parity = RecordingEnumText.ParseParity(r.GetString(4), _logger, batchId),
            StopBits = RecordingEnumText.ParseStopBits(r.GetString(5), _logger, batchId)
        };

        var settingsLabel = settings.ShortDescription.Replace(' ', '-');

        return new RecordingBatchSummary(
            batchId,
            portLabel,
            settingsLabel,
            DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8));
    }
}
