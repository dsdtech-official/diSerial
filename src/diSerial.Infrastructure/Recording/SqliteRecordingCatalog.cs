using System.Globalization;
using System.Text;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Recording;

// List and manage recordings.
public sealed class SqliteRecordingCatalog(IAppPaths paths, ILogger? logger = null)
    : IRecordingCatalog
{
    private const int DeleteChunkSize = 500;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    // List matching recorded batches.
    public async Task<IReadOnlyList<RecordingBatchListItem>> ListBatchesAsync(
        RecordingBatchFilter filter, CancellationToken cancellationToken = default)
    {
        if (!DatabaseExists) return [];

        await using var conn = await OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var sql = new StringBuilder("""
            SELECT b.id, b.session_kind, b.port_a, b.port_b, b.alias_a, b.alias_b,
                   b.baud_rate, b.data_bits, b.parity, b.stop_bits, b.started_at, b.ended_at,
                   (SELECT COUNT(*) FROM frame f WHERE f.batch_id = b.id) AS frame_count
            FROM batch b
            """);

        var where = new List<string>();

        if (filter.From is { } from)
        {
            where.Add("b.started_at >= $from");
            cmd.Parameters.AddWithValue("$from", ToBatchTimestamp(from));
        }

        if (filter.To is { } to)
        {
            where.Add("b.started_at < $to");
            cmd.Parameters.AddWithValue("$to", ToBatchTimestamp(to));
        }

        if (where.Count > 0) sql.Append("\nWHERE ").Append(string.Join("\n  AND ", where));

        sql.Append("\nORDER BY b.started_at DESC, b.id DESC;");
        cmd.CommandText = sql.ToString();

        var items = new List<RecordingBatchListItem>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var id = r.GetInt64(0);
            var portB = r.IsDBNull(3) ? null : r.GetString(3);

            items.Add(new RecordingBatchListItem(
                id,
                ParseSessionKind(r.GetString(1), portB, id),
                r.GetString(2),
                portB,
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                new SerialPortSettings
                {
                    BaudRate = r.GetInt32(6),
                    DataBits = r.GetInt32(7),
                    Parity = RecordingEnumText.ParseParity(r.GetString(8), _logger, id),
                    StopBits = RecordingEnumText.ParseStopBits(r.GetString(9), _logger, id)
                },
                ParseTimestamp(r.GetString(10)),
                r.IsDBNull(11) ? null : ParseTimestamp(r.GetString(11)),
                r.GetInt64(12)));
        }

        return items;
    }

    // Delete the requested batches.
    public async Task<int> DeleteBatchesAsync(
        IReadOnlyCollection<long> batchIds, CancellationToken cancellationToken = default)
    {
        if (batchIds.Count == 0 || !DatabaseExists) return 0;

        await using var conn = await OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        var deleted = 0;
        foreach (var chunk in batchIds.Distinct().Chunk(DeleteChunkSize))
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;

            var names = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                names[i] = $"$id{i}";
                cmd.Parameters.AddWithValue(names[i], chunk[i]);
            }

            cmd.CommandText = $"DELETE FROM batch WHERE id IN ({string.Join(", ", names)});";
            deleted += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted {Count} recording batches; the database file does not shrink until it is compacted.",
            deleted);

        return deleted;
    }

    // Read the database size.
    public Task<long> GetDatabaseSizeAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseExists) return Task.FromResult(0L);

        try
        {
            return Task.FromResult(new FileInfo(paths.RecordingDatabasePath).Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not measure the recording database file.");
            return Task.FromResult(0L);
        }
    }

    // Compact the recording database.
    public async Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseExists) return;

        try
        {
            await using var conn = await OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = "VACUUM;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (IsBusy(ex))
        {
            _logger.LogWarning(
                ex, "Could not compact the recording database: it is held by something else.");

            throw new RecordingDatabaseBusyException(ex);
        }

        _logger.LogInformation("Compacted the recording database.");
    }

    private bool DatabaseExists => File.Exists(paths.RecordingDatabasePath);

    public const int BusyTimeoutSeconds = 5;

    // Check for SQLite busy errors.
    private static bool IsBusy(SqliteException ex) =>
        ex.SqliteErrorCode is 5 or 6;

    // Open the recording database.
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var conn = new SqliteConnection(
            $"Data Source={paths.RecordingDatabasePath};Default Timeout={BusyTimeoutSeconds}");
        await conn.OpenAsync(cancellationToken);

        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);

        RecordingDatabase.Ensure(conn, paths.RecordingDatabasePath);

        return conn;
    }

    // Read a scalar database result.
    private static async Task<long> ScalarAsync(
        SqliteConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    // Format a batch timestamp.
    private static string ToBatchTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // Parse a recorded timestamp.
    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    // Parse a stored session type.
    private SessionKind ParseSessionKind(string stored, string? portB, long batchId)
    {
        if (Enum.TryParse<SessionKind>(stored, out var kind)) return kind;

        var inferred = portB is null ? SessionKind.Terminal : SessionKind.Monitor;
        _logger.LogWarning(
            "Batch {BatchId} has an unrecognised session_kind {Stored}; treating it as {Inferred} because port_b is {PortBState}.",
            batchId, stored, inferred, portB is null ? "absent" : "present");

        return inferred;
    }
}
