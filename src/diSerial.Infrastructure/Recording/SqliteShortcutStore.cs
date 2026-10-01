using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Persist saved commands.
public sealed class SqliteShortcutStore(IAppPaths paths, ILoggerFactory loggerFactory)
    : IShortcutStore
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<SqliteShortcutStore>();

    // Load saved send shortcuts.
    public IReadOnlyList<Shortcut> Load()
    {
        var items = new List<Shortcut>();

        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RecordingSchema.LoadShortcuts;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var length = (int)reader.GetBytes(2, 0, null, 0, 0);
                var payload = new byte[length];
                if (length > 0) reader.GetBytes(2, 0, payload, 0, length);

                items.Add(new Shortcut(reader.GetInt64(0), reader.GetString(1), payload, reader.GetInt64(3)));
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not read shortcuts; continuing without them");
        }

        return items;
    }

    // Save a send shortcut.
    public ShortcutAddResult Add(string name, byte[] payload)
    {
        if (!IsNameWellFormed(name)) return ShortcutAddResult.InvalidName;
        if (payload.Length == 0) return ShortcutAddResult.EmptyPayload;

        try
        {
            using var connection = Open();

            if (Count(connection) >= ShortcutLimits.MaxCount) return ShortcutAddResult.Full;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = RecordingSchema.InsertShortcut;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$payload", payload);
            cmd.Parameters.AddWithValue("$ordinal", NextOrdinal(connection));
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

            cmd.ExecuteNonQuery();
            return ShortcutAddResult.Added;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            _logger.LogDebug(e, "Shortcut name {Name} is already taken; add refused.", name);
            return ShortcutAddResult.DuplicateName;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not store shortcut {Name}", name);
            return ShortcutAddResult.StorageFailed;
        }
    }

    // Rename a saved send shortcut.
    public bool Rename(long id, string name)
    {
        if (!IsNameWellFormed(name)) return false;

        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RecordingSchema.RenameShortcut;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", id);

            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            _logger.LogDebug(e, "Shortcut rename to {Name} refused: that name is taken.", name);
            return false;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not rename shortcut {Id}", id);
            return false;
        }
    }

    // Delete a saved send shortcut.
    public void Delete(long id) => Execute(RecordingSchema.DeleteShortcut, id, "delete");

    // Move a shortcut to the front.
    public void MoveToFront(long id) => Execute(RecordingSchema.ShortcutToFront, id, "move");

    // Execute a shortcut SQL update.
    private void Execute(string sql, long id, string what)
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not {What} shortcut {Id}", what, id);
        }
    }

    // Move a shortcut up one place.
    public void MoveForwardOne(long id)
    {
        var items = Load();
        var index = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id) { index = i; break; }
        }

        if (index <= 0) return;

        var self = items[index];
        var ahead = items[index - 1];

        try
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            SetOrdinal(connection, self.Id, ahead.Ordinal);
            SetOrdinal(connection, ahead.Id, self.Ordinal);

            if (ahead.Ordinal == self.Ordinal) SetOrdinal(connection, self.Id, self.Ordinal - 1);

            transaction.Commit();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not reorder shortcut {Id}", id);
        }
    }

    // Validate the command name.
    private static bool IsNameWellFormed(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Trim().Length <= ShortcutLimits.MaxNameLength;
    }

    // Set a stored item's position.
    private static void SetOrdinal(SqliteConnection connection, long id, long ordinal)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE shortcut SET ordinal = $ordinal WHERE id = $id;";
        cmd.Parameters.AddWithValue("$ordinal", ordinal);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // Count stored send shortcuts.
    private static int Count(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.CountShortcuts;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Get the next stored position.
    private static long NextOrdinal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.NextShortcutOrdinal;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // Identify handled storage errors.
    private static bool IsStorageFailure(Exception ex) =>
        ex is SchemaTooNewException or SqliteException or IOException
            or UnauthorizedAccessException or FormatException;

    // Open the shortcut database.
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={paths.RecordingDatabasePath};Default Timeout={SqliteSendHistoryStore.BusyTimeoutSeconds}");
        connection.Open();

        RecordingDatabase.Ensure(connection, paths.RecordingDatabasePath, _logger);

        return connection;
    }
}
