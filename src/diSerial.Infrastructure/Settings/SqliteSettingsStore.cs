using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Settings;

// Persist application settings.
public sealed class SqliteSettingsStore : ISettingsStore
{
    public const int SchemaVersion = 2;

    public const string QuarantineSuffix = ".corrupt";

    // Build the settings backup path.
    public static string BackupPathFor(string databasePath, int fromVersion) =>
        $"{databasePath}.v{fromVersion}.bak";

    private const string Pragmas = "PRAGMA synchronous = FULL;";

    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS param (
            id            TEXT PRIMARY KEY,
            value         TEXT NOT NULL,
            note          TEXT NOT NULL DEFAULT '',
            value_type    TEXT NOT NULL DEFAULT '',
            updated_at    TEXT NOT NULL DEFAULT ''
        );
        """;

    private static readonly string[] MigrateV1ToV2 =
    [
        "ALTER TABLE param ADD COLUMN note          TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE param ADD COLUMN value_type    TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE param ADD COLUMN updated_at    TEXT NOT NULL DEFAULT '';"
    ];

    private const string SelectAll = "SELECT id, value FROM param;";

    private const string UpsertOne = """
        INSERT INTO param (id, value, note, value_type, updated_at)
        VALUES ($id, $value, $note, $type, $now)
        ON CONFLICT (id) DO UPDATE SET
            value      = $value,
            note       = $note,
            value_type = $type,
            updated_at = $now;
        """;

    private const string SeedOne = """
        INSERT INTO param (id, value, note, value_type, updated_at)
        VALUES ($id, $default, $note, $type, '')
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string RefreshOne = """
        UPDATE param SET
            note       = $note,
            value_type = $type,
            value      = CASE WHEN updated_at = '' THEN $default ELSE value END
        WHERE id = $id
          AND (note <> $note
               OR value_type <> $type
               OR (updated_at = '' AND value <> $default));
        """;

    private readonly IAppPaths _paths;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private bool _refusedAsTooNew;

    // Initialize settings storage.
    public SqliteSettingsStore(IAppPaths paths, ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _logger = loggerFactory.CreateLogger("Settings.Store");
    }

    // Read all stored settings.
    public IReadOnlyDictionary<string, string> LoadAll()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        NoteAbandonedJsonFile();

        lock (_gate)
        {
            if (_refusedAsTooNew) return rows;

            try
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = SelectAll;

                using var reader = cmd.ExecuteReader();
                while (reader.Read()) rows[reader.GetString(0)] = reader.GetString(1);
            }
            catch (SchemaTooNewException ex)
            {
                _refusedAsTooNew = true;

                _logger.LogError(ex,
                    "The settings database at {Path} was written by a NEWER version of this "
                    + "program (file format {Found}, this version understands up to "
                    + "{Supported}). It is left untouched and nothing will be saved this run; "
                    + "this build's defaults are in use. Upgrade the program, or point it at a "
                    + "different data directory.",
                    _paths.SettingsDatabasePath, ex.Found, ex.Supported);

                return rows;
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                var outcome = Quarantine();

                _logger.LogError(ex,
                    "Could not open the settings database at {Path}; {Outcome}. Starting from "
                    + "defaults -- all stored settings for this user are lost.",
                    _paths.SettingsDatabasePath, outcome);

                return rows;
            }
        }

        return rows;
    }

    // Sync the settings catalog.
    public void Sync(IReadOnlyDictionary<string, SettingsRowInfo> catalog)
    {
        if (catalog.Count == 0) return;

        lock (_gate)
        {
            if (_refusedAsTooNew) return;

            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                var seeded = 0;
                var refreshed = 0;

                foreach (var (id, row) in catalog)
                {
                    using var seed = connection.CreateCommand();
                    seed.CommandText = SeedOne;
                    seed.Parameters.AddWithValue("$id", id);
                    seed.Parameters.AddWithValue("$default", row.DefaultValue);
                    seed.Parameters.AddWithValue("$note", row.Note);
                    seed.Parameters.AddWithValue("$type", row.ValueType);
                    seeded += seed.ExecuteNonQuery();

                    using var refresh = connection.CreateCommand();
                    refresh.CommandText = RefreshOne;
                    refresh.Parameters.AddWithValue("$id", id);
                    refresh.Parameters.AddWithValue("$default", row.DefaultValue);
                    refresh.Parameters.AddWithValue("$note", row.Note);
                    refresh.Parameters.AddWithValue("$type", row.ValueType);
                    refreshed += refresh.ExecuteNonQuery();
                }

                transaction.Commit();

                if (seeded > 0 || refreshed > 0)
                    _logger.LogInformation(
                        "Settings synced with the catalog: {Seeded} seeded, {Refreshed} refreshed.",
                        seeded, refreshed);
            }
            catch (SchemaTooNewException ex)
            {
                _refusedAsTooNew = true;
                _logger.LogError(ex,
                    "Not syncing the catalog into {Path}: it was written by a newer version "
                    + "(file format {Found}, this version understands up to {Supported}).",
                    _paths.SettingsDatabasePath, ex.Found, ex.Supported);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                _logger.LogWarning(ex, "Could not sync the settings table with the catalog.");
            }
        }
    }

    // Insert or update settings.
    public void Upsert(
        IReadOnlyDictionary<string, string> changedRows,
        IReadOnlyDictionary<string, SettingsRowInfo> info)
    {
        if (changedRows.Count == 0) return;

        lock (_gate)
        {
            if (_refusedAsTooNew) return;

            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                var now = DateTimeOffset.UtcNow.ToString("O");

                foreach (var (id, value) in changedRows)
                {
                    var row = info.TryGetValue(id, out var found)
                        ? found
                        : new SettingsRowInfo(string.Empty, string.Empty, string.Empty);

                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = UpsertOne;
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$value", value);
                    cmd.Parameters.AddWithValue("$note", row.Note);
                    cmd.Parameters.AddWithValue("$type", row.ValueType);
                    cmd.Parameters.AddWithValue("$now", now);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();

                _logger.LogInformation(
                    "Settings saved to {Path}: {Count} row(s) [{Ids}].",
                    _paths.SettingsDatabasePath, changedRows.Count,
                    string.Join(", ", changedRows.Keys.Order(StringComparer.Ordinal)));
            }
            catch (SchemaTooNewException ex)
            {
                _refusedAsTooNew = true;
                _logger.LogError(ex,
                    "Not writing settings to {Path}: it was written by a newer version "
                    + "(file format {Found}, this version understands up to {Supported}).",
                    _paths.SettingsDatabasePath, ex.Found, ex.Supported);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                _logger.LogError(ex, "Failed to write settings to {Path}.",
                    _paths.SettingsDatabasePath);
            }
        }
    }

    // Open the settings database.
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsDatabasePath)!);

        var connection = new SqliteConnection(
            $"Data Source={_paths.SettingsDatabasePath};Pooling=False");

        connection.Open();

        try
        {
            using var pragmas = connection.CreateCommand();
            pragmas.CommandText = Pragmas;
            pragmas.ExecuteNonQuery();

            EnsureSchema(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return connection;
    }

    // Ensure the settings schema.
    private void EnsureSchema(SqliteConnection connection)
    {
        var found = ReadVersion(connection);

        if (found > SchemaVersion)
        {
            throw new SchemaTooNewException(_paths.SettingsDatabasePath, found, SchemaVersion);
        }

        var isNewFile = !TableExists(connection, "param");

        if (!isNewFile && found == SchemaVersion) return;

        if (!isNewFile) BackUpBeforeMigrating(found);

        using var transaction = connection.BeginTransaction();

        if (isNewFile)
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = Ddl;
            ddl.ExecuteNonQuery();
        }
        else
        {
            for (var step = found + 1; step <= SchemaVersion; step++) ApplyStep(connection, step);

            _logger.LogInformation(
                "Migrated the settings database from schema {From} to {To}.", found, SchemaVersion);
        }

        using var stamp = connection.CreateCommand();
        stamp.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        stamp.ExecuteNonQuery();

        transaction.Commit();
    }

    // Read the schema version.
    private static int ReadVersion(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(read.ExecuteScalar());
    }

    // Check database table existence.
    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    // Apply a database migration step.
    private void ApplyStep(SqliteConnection connection, int step)
    {
        switch (step)
        {
            case 1:
                break;

            case 2:
                foreach (var statement in MigrateV1ToV2) ApplyColumn(connection, statement);
                break;

            default:
                throw new InvalidOperationException(
                    $"No migration is defined for settings schema step {step}.");
        }
    }

    // Add a missing settings column.
    private void ApplyColumn(SqliteConnection connection, string statement)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = statement;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            _logger.LogDebug(ex, "Migration step already applied: {Statement}", statement);
        }
    }

    // Back up before schema migration.
    private void BackUpBeforeMigrating(int fromVersion)
    {
        var backup = BackupPathFor(_paths.SettingsDatabasePath, fromVersion);

        try
        {
            File.Copy(_paths.SettingsDatabasePath, backup, overwrite: true);
            _logger.LogInformation(
                "Copied the settings database to {Backup} before migrating from schema {From}.",
                backup, fromVersion);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex,
                "Could not copy the settings database to {Backup}; migrating without one.",
                backup);
        }
    }

    // Report obsolete settings files.
    private void NoteAbandonedJsonFile()
    {
        try
        {
            var legacy = Path.Combine(_paths.ConfigDirectory, "settings.json");
            if (!File.Exists(legacy)) return;

            _logger.LogDebug(
                "{Path} is still on disk from before 2026-08-07 and is NOT read any more; "
                + "settings live in {Current}. Nothing will be migrated from it.",
                legacy, _paths.SettingsDatabasePath);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogDebug(ex, "Could not check for a leftover settings.json.");
        }
    }

    // Move an incompatible database.
    private string Quarantine()
    {
        var quarantine = _paths.SettingsDatabasePath + QuarantineSuffix;

        try
        {
            if (!File.Exists(_paths.SettingsDatabasePath))
                return "there was no file to move aside, so the failure was creating it";

            File.Move(_paths.SettingsDatabasePath, quarantine, overwrite: true);
            return $"moved it to {quarantine}";
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _logger.LogWarning(ex, "Could not move the unreadable settings database aside.");
            return "it could not be moved aside and stays where it is";
        }
    }

    // Identify handled storage errors.
    private static bool IsStorageFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException or FormatException;
}
