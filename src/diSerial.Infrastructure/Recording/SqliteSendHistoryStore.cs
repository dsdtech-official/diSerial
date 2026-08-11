using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// <see cref="ISendHistoryStore"/> on the recordings database. Contract, and the reasoning
/// behind identity and the synchronous signatures, is on the interface.
///
/// <b>A connection per call, not one held open.</b> These calls are rare (once per session
/// load, once per send) and touch at most a hundred short rows, while
/// <see cref="SqliteSessionRecorder"/> may hold its own connection to the same file for
/// minutes at a time. Opening briefly keeps the two independent — WAL, already on for this
/// file, is what makes a reader and a writer coexist — and it removes any question about
/// whether this store outlives a recording. A held connection would buy nothing measurable
/// and would couple two lifetimes that have no reason to know about each other.
/// </summary>
public sealed class SqliteSendHistoryStore(IAppPaths paths, ILoggerFactory loggerFactory)
    : ISendHistoryStore
{
    /// <summary>
    /// Rows kept on disk (user decision 2026-08-02). The dropdown shows fewer — see
    /// <see cref="ISendHistoryStore.Load"/>: the tail stays around so an entry that fell off
    /// the visible list is still there to be promoted when it is next used.
    /// </summary>
    public const int Capacity = 100;

    private readonly ILogger _logger = loggerFactory.CreateLogger("Recording.SendHistory");
    private readonly Lock _gate = new();

    public IReadOnlyList<SendHistoryEntry> Load(int limit)
    {
        if (limit <= 0) return [];

        // ⚠️ Clamped, not `new List<>(limit)`. The caller's limit is a request, not a promise
        // about size: `Load(int.MaxValue)` — a perfectly reasonable way to say "everything" —
        // asked for an int.MaxValue-element array and threw. The table cannot exceed
        // Capacity anyway, so that is the only sensible hint. Found by a test, not by reading.
        var entries = new List<SendHistoryEntry>(Math.Min(limit, Capacity));

        try
        {
            lock (_gate)
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = RecordingSchema.LoadSendHistory;
                cmd.Parameters.AddWithValue("$limit", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    entries.Add(new SendHistoryEntry(
                        reader.GetString(0),
                        reader.GetInt64(1) != 0,
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        (int)reader.GetInt64(3),
                        DateTimeOffset.Parse(reader.GetString(4), null,
                            System.Globalization.DateTimeStyles.RoundtripKind)));
                }
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // Degrade to "no history", never to "cannot send". See the interface remarks.
            _logger.LogWarning(ex, "Could not read send history; continuing without it");
            return [];
        }

        return entries;
    }

    public void Record(string text, bool isHexMode, string? lineEnding)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            lock (_gate)
            {
                using var connection = Open();
                var now = DateTimeOffset.UtcNow.ToString("O");

                using (var upsert = connection.CreateCommand())
                {
                    upsert.CommandText = RecordingSchema.UpsertSendHistory;
                    upsert.Parameters.AddWithValue("$text", text);
                    upsert.Parameters.AddWithValue("$isHex", isHexMode ? 1 : 0);
                    upsert.Parameters.AddWithValue("$lineEnding", (object?)lineEnding ?? DBNull.Value);
                    upsert.Parameters.AddWithValue("$now", now);
                    upsert.ExecuteNonQuery();
                }

                using var trim = connection.CreateCommand();
                trim.CommandText = RecordingSchema.TrimSendHistory;
                trim.Parameters.AddWithValue("$cap", Capacity);
                trim.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // The bytes already went out; failing here must not look like a failed send.
            _logger.LogWarning(ex, "Could not record send history entry");
        }
    }

    public void Delete(string text, bool isHexMode)
    {
        try
        {
            lock (_gate)
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = RecordingSchema.DeleteSendHistory;
                cmd.Parameters.AddWithValue("$text", text);
                cmd.Parameters.AddWithValue("$isHex", isHexMode ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // ⚠️ Worth a louder level than the other two: the user asked for something to be
            // forgotten and it was not. Silence here would be the tool lying about a deletion.
            _logger.LogError(ex, "Could not delete send history entry");
        }
    }

    public int Count()
    {
        try
        {
            lock (_gate)
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = RecordingSchema.CountSendHistory;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // Only feeds a confirmation message; a wrong number must not block the dialog.
            _logger.LogWarning(ex, "Could not count send history entries");
            return 0;
        }
    }

    /// <summary>
    /// ⛔ <b>Deliberately not wrapped in the usual catch.</b> Every other method here degrades
    /// quietly; this one must not. See <see cref="ISendHistoryStore.Clear"/> — a silent failure
    /// would tell the user their payloads are gone while they are still on disk.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RecordingSchema.ClearSendHistory;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Opens the file and makes sure the table is there.
    ///
    /// The DDL runs on every open rather than once per process: it is
    /// <c>CREATE TABLE IF NOT EXISTS</c> against an open connection, so the cost is noise, and
    /// it removes an ordering dependency — <b>this store may well run before anything has ever
    /// been recorded</b>, so the database file itself may not exist yet.
    /// </summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        connection.Open();

        using var ddl = connection.CreateCommand();
        ddl.CommandText = RecordingSchema.SendHistoryDdl;
        ddl.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// What counts as "storage misbehaved" rather than "this code is wrong".
    ///
    /// ⚠️ Deliberately not a bare <c>catch</c>: a bug in this file should surface, not hide
    /// behind a warning about the disk. Locked file, missing directory, and permission
    /// problems are the real-world cases and they must not reach the send path.
    /// </summary>
    private static bool IsStorageFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException or FormatException;
}
