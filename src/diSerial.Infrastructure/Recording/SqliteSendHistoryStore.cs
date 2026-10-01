using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Persist sent command history.
public sealed class SqliteSendHistoryStore(IAppPaths paths, ILoggerFactory loggerFactory)
    : ISendHistoryStore
{
    public const int Capacity = 100;

    private readonly ILogger _logger = loggerFactory.CreateLogger("Recording.SendHistory");
    private readonly Lock _gate = new();

    internal Func<DateTimeOffset> UtcNow { get; init; } = static () => DateTimeOffset.UtcNow;

    // Choose the next history time.
    internal static DateTimeOffset NextLastUsedAt(DateTimeOffset now, DateTimeOffset? latest) =>
        latest is { } previous && previous >= now ? previous.AddTicks(1) : now;

    // Read the latest history time.
    private static DateTimeOffset? LatestLastUsedAt(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.LatestSendHistoryUse;

        return cmd.ExecuteScalar() is string text
            ? DateTimeOffset.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : null;
    }

    // Load saved send history.
    public IReadOnlyList<SendHistoryEntry> Load(int limit)
    {
        if (limit <= 0) return [];

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
            _logger.LogWarning(ex, "Could not read send history; continuing without it");
            return [];
        }

        return entries;
    }

    // Save a send history entry.
    public void Record(string text, bool isHexMode, string? lineEnding)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            lock (_gate)
            {
                using var connection = Open();
                var now = NextLastUsedAt(UtcNow(), LatestLastUsedAt(connection)).ToString("O");

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
            _logger.LogWarning(ex, "Could not record send history entry");
        }
    }

    // Delete a send history entry.
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
            _logger.LogError(ex, "Could not delete send history entry");
        }
    }

    // Count saved send entries.
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
            _logger.LogWarning(ex, "Could not count send history entries");
            return 0;
        }
    }

    // Clear saved send history.
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

    public const int BusyTimeoutSeconds = 1;

    // Open the send history database.
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={paths.RecordingDatabasePath};Default Timeout={BusyTimeoutSeconds}");
        connection.Open();

        RecordingDatabase.Ensure(connection, paths.RecordingDatabasePath, _logger);

        return connection;
    }

    // Identify handled storage errors.
    private static bool IsStorageFailure(Exception ex) =>
        ex is SchemaTooNewException or SqliteException or IOException
            or UnauthorizedAccessException or FormatException;
}
