using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> on its own SQLite file. The contract, and why the seam only
/// moves strings, are on the interface.
///
/// <para><b>Its own file, not a table in the recordings database</b> — the reason is on
/// <see cref="IAppPaths.SettingsDatabasePath"/> and it is about lifetimes, not tidiness.</para>
///
/// <para><b>A connection per call.</b> Same reasoning as
/// <c>SqliteSendHistoryStore</c>: the calls are rare (one read at startup, one short write per
/// change) and holding a connection open for the life of the process would keep a file lock for
/// no measurable gain. It also keeps a second running instance able to write.</para>
///
/// <para>⛔ <b>No method here throws.</b> Settings are a convenience: failing to read them must
/// not stop the app from starting, and failing to write them costs the next launch's memory,
/// not the current session.</para>
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    /// <summary>
    /// Storage schema version, in SQLite's own <c>user_version</c>.
    ///
    /// <para><b>1</b> — <c>param(id, value)</c>.<br/>
    /// <b>2</b> (2026-08-07, user request) — adds the descriptive columns
    /// <c>note</c> / <c>value_type</c> and the audit column <c>updated_at</c>, so the table
    /// can be read without the source.</para>
    ///
    /// <para>⭐ Version 2 is the first thing that ever <i>read</i> this pragma. It was stamped
    /// in version 1 with the note "there is nothing to migrate from yet, it exists so the first
    /// migration has somewhere to look" — this is that migration.</para>
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    /// Appended to the database path when it cannot be opened. Public so tests name the same
    /// file the implementation does rather than repeating the literal.
    /// </summary>
    public const string QuarantineSuffix = ".corrupt";

    /// <summary>
    /// ⚠️ <b>Deliberately NOT WAL</b>, unlike the recordings database. WAL exists there so a
    /// reader and a long-running writer can coexist; here there is one read at startup and rare
    /// one-row writes, so it would buy nothing and cost two things that matter:
    /// <list type="number">
    ///   <item>two extra files (<c>-wal</c>, <c>-shm</c>) sitting next to the database, which
    ///         <see cref="Quarantine"/> would have to move as a set or leave orphaned beside a
    ///         fresh database that has nothing to do with them;</item>
    ///   <item>a checkpoint story for a file that is idle almost all the time.</item>
    /// </list>
    /// The default rollback journal leaves nothing behind at rest, and two processes still
    /// serialise correctly on it — which is all the multi-instance case needs.
    ///
    /// <para><c>synchronous = FULL</c> because writes are rare and tiny: there is no throughput
    /// to trade away, and the thing being protected is a setting the user just asked for.</para>
    ///
    /// <para>⚠️ <c>value</c> is <c>NOT NULL</c>. "Absent" is expressed by the row not existing,
    /// never by a null value — one way to say one thing. The catalog turns an empty string into
    /// the null-language case; that is the catalog's business, not the store's.</para>
    /// </summary>
    private const string Ddl = """
        PRAGMA synchronous = FULL;

        CREATE TABLE IF NOT EXISTS param (
            id            TEXT PRIMARY KEY,
            value         TEXT NOT NULL,
            note          TEXT NOT NULL DEFAULT '',
            value_type    TEXT NOT NULL DEFAULT '',
            updated_at    TEXT NOT NULL DEFAULT ''
        );
        """;

    /// <summary>
    /// v1 → v2: add the three descriptive columns to a table that already exists.
    ///
    /// <para>⚠️ Each one is a separate statement because SQLite's <c>ALTER TABLE</c> adds one
    /// column at a time, and each needs a non-null default so existing rows stay valid.</para>
    /// </summary>
    private static readonly string[] MigrateV1ToV2 =
    [
        "ALTER TABLE param ADD COLUMN note          TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE param ADD COLUMN value_type    TEXT NOT NULL DEFAULT '';",
        "ALTER TABLE param ADD COLUMN updated_at    TEXT NOT NULL DEFAULT '';"
    ];

    // ⛔ Only `value` is selected. The descriptive columns are written by us and never read
    // back into behaviour -- see the remarks on SettingsRowInfo for why that matters.
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

    /// <summary>
    /// Seeds a parameter that has no row yet. ⭐ <c>updated_at = ''</c> is the whole point:
    /// it marks the row as "nobody chose this, the value is just today's default".
    ///
    /// <para><c>DO NOTHING</c> on conflict so this is safe to run when another instance seeded
    /// first — the refresh below is what keeps an existing row current.</para>
    /// </summary>
    private const string SeedOne = """
        INSERT INTO param (id, value, note, value_type, updated_at)
        VALUES ($id, $default, $note, $type, '')
        ON CONFLICT (id) DO NOTHING;
        """;

    /// <summary>
    /// ⛔ <c>UPDATE</c> only, and the value is touched <b>only for a row nobody chose</b>.
    ///
    /// <para>⚠️ The <c>updated_at = ''</c> in the SET clause's guard is what makes a changed
    /// default reach a user who never touched the parameter. Drop it and every seeded row is
    /// frozen at whatever default was current the day it was written.</para>
    ///
    /// <para>⚠️ <c>updated_at</c> itself is never written here: refreshing a description, or
    /// following a new default, is not the user changing anything.</para>
    /// </summary>
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

    public SqliteSettingsStore(IAppPaths paths, ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _logger = loggerFactory.CreateLogger("Settings.Store");
    }

    public IReadOnlyDictionary<string, string> LoadAll()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        NoteAbandonedJsonFile();

        lock (_gate)
        {
            try
            {
                using var connection = Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = SelectAll;

                using var reader = cmd.ExecuteReader();
                while (reader.Read()) rows[reader.GetString(0)] = reader.GetString(1);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                // The whole file is unreadable, which is a different case from a single row
                // this build cannot understand (that one is handled by the caller, per row).
                //
                // ⭐ Rename rather than delete (user decision 2026-08-07). The user also decided
                // against any reset or export entry point in the UI, so the renamed file is the
                // ONLY forensic trace left if "my settings keep disappearing" is ever reported.
                // Deleting would cost nothing today and leave nothing to look at later.
                //
                // ⚠️ The log line lives HERE rather than inside Quarantine(): the swallowed
                // exception scanner reads catch blocks lexically and cannot follow a helper.
                // ⛔ Whitelisting this file instead would disarm the two load-bearing catches
                // further down, which is not a trade worth making for one call.
                var outcome = Quarantine();

                // Error, not Warning: every stored setting for this user is gone.
                _logger.LogError(ex,
                    "Could not open the settings database at {Path}; {Outcome}. Starting from "
                    + "defaults -- all stored settings for this user are lost.",
                    _paths.SettingsDatabasePath, outcome);

                return rows;
            }
        }

        return rows;
    }

    public void Sync(IReadOnlyDictionary<string, SettingsRowInfo> catalog)
    {
        if (catalog.Count == 0) return;

        lock (_gate)
        {
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

                // ⚠️ One transaction for the whole sweep: a crash halfway must not leave a table
                // that is neither the old shape nor the new one.
                transaction.Commit();

                // Silent on a normal launch -- everything already matches and both counts are
                // zero. It speaks only when a build actually added a parameter or changed one.
                if (seeded > 0 || refreshed > 0)
                    _logger.LogInformation(
                        "Settings synced with the catalog: {Seeded} seeded, {Refreshed} refreshed.",
                        seeded, refreshed);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                // ⚠️ Degrades to "the table is not complete", which the caller survives: a
                // missing row still means "use this build's default".
                _logger.LogWarning(ex, "Could not sync the settings table with the catalog.");
            }
        }
    }

    public void Upsert(
        IReadOnlyDictionary<string, string> changedRows,
        IReadOnlyDictionary<string, SettingsRowInfo> info)
    {
        if (changedRows.Count == 0) return;

        lock (_gate)
        {
            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                var now = DateTimeOffset.UtcNow.ToString("O");

                foreach (var (id, value) in changedRows)
                {
                    // ⚠️ Missing description falls back to empty strings rather than throwing:
                    // failing to SAVE A USER SETTING because we lack a label for it would be
                    // wildly out of proportion. The catalog test is what keeps them present.
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

                // Log what was written, not just that a write happened. "My setting did not
                // stick" has several causes (never triggered, wrote elsewhere, wrote then read
                // wrong) and only the ids tell them apart. The values are the user's own
                // settings, already visible in the UI, so there is nothing sensitive to keep out.
                _logger.LogInformation(
                    "Settings saved to {Path}: {Count} row(s) [{Ids}].",
                    _paths.SettingsDatabasePath, changedRows.Count,
                    string.Join(", ", changedRows.Keys.Order(StringComparer.Ordinal)));
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                // Error, not Warning: this is data loss. It only costs the next launch, but the
                // user asked for something to be remembered and it will not be.
                _logger.LogError(ex, "Failed to write settings to {Path}.",
                    _paths.SettingsDatabasePath);
            }
        }
    }

    /// <summary>
    /// Opens the database and makes sure the table is there.
    ///
    /// <para>⛔ <b><c>Pooling=False</c> is load-bearing, not a tuning knob.</b> Pooled
    /// connections keep the file handle open after <c>Dispose</c>, and a held handle makes
    /// <see cref="Quarantine"/> unable to move an unreadable database aside — the recovery path
    /// would fail on Windows precisely when it is needed. The calls here are one read at startup
    /// and rare one-row writes, so there is no pooling benefit to give up.</para>
    ///
    /// <para>⚠️ <b>The connection is disposed if setup throws.</b> <c>connection.Open()</c>
    /// succeeds on a file that is not a database — the failure surfaces on the first statement.
    /// Letting that propagate from a local variable would leak an open handle on every attempt,
    /// with the same consequence as above: the file stays locked and cannot be moved aside.
    /// Found by <c>SqliteSettingsStoreTests.UnreadableDatabase_IsMovedAsideAndReplaced</c>,
    /// not by reading the code.</para>
    /// </summary>
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsDatabasePath)!);

        var connection = new SqliteConnection(
            $"Data Source={_paths.SettingsDatabasePath};Pooling=False");

        connection.Open();

        try
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = Ddl;
            ddl.ExecuteNonQuery();

            Migrate(connection);

            using var version = connection.CreateCommand();
            version.CommandText = $"PRAGMA user_version = {SchemaVersion};";
            version.ExecuteNonQuery();
        }
        catch
        {
            // ⚠️ Deliberately catches everything and rethrows: the point is releasing the
            // handle, and which exception it was is the caller's business. `throw;` keeps the
            // original stack, so the swallowed-exception rule's second exit applies.
            connection.Dispose();
            throw;
        }

        return connection;
    }

    /// <summary>
    /// Moves an unreadable database aside so the next <see cref="Open"/> creates a fresh one.
    ///
    /// <para>⚠️ Only ever ONE quarantined file is kept, and a second failure overwrites it.
    /// The alternative — numbered copies — turns a database that fails to open every launch
    /// into unbounded litter in the user's AppData, which is a worse failure than losing the
    /// older of two copies of the same broken file.</para>
    /// </summary>
    /// <summary>
    /// Brings an older database up to <see cref="SchemaVersion"/>.
    ///
    /// <para>⚠️ <c>CREATE TABLE IF NOT EXISTS</c> in the DDL only helps a <b>new</b> file: a
    /// database created by an earlier build already has the table, so the statement is a no-op
    /// and the new columns would simply never appear. ⛔ That failure is silent — every write
    /// would then throw "no such column" and be swallowed as a storage failure.</para>
    ///
    /// <para>Each step is tried on its own and a step that fails because the column is already
    /// there is not an error: it means someone reached this schema by another route.</para>
    /// </summary>
    private void Migrate(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        var found = Convert.ToInt32(read.ExecuteScalar());

        if (found >= SchemaVersion) return;

        foreach (var statement in MigrateV1ToV2)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
            {
                // Already present. Log so a puzzling half-migrated file leaves a trace, but do
                // not stop: the remaining columns still need adding.
                _logger.LogDebug(ex, "Migration step already applied: {Statement}", statement);
            }
        }

        _logger.LogInformation(
            "Migrated the settings database from schema {From} to {To}.", found, SchemaVersion);
    }

    /// <summary>
    /// Says once per launch that a <c>settings.json</c> is lying around and is not being read.
    ///
    /// <para>⚠️ <b>There is no migration and none is wanted</b> (user decision 2026-08-07: the
    /// app is unreleased, so there are no users whose settings could be lost). The file is left
    /// alone rather than deleted — it is the user's, and deleting files nobody asked us to
    /// delete is not this layer's call.</para>
    ///
    /// <para>⭐ The log line earns its place on developer machines specifically: an orphaned
    /// <c>settings.json</c> that still <i>looks</i> live is exactly the sort of thing someone
    /// edits, restarts, and then spends an hour asking why nothing changed.</para>
    /// </summary>
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
            // Only a courtesy message; never let it affect startup.
            _logger.LogDebug(ex, "Could not check for a leftover settings.json.");
        }
    }

    private string Quarantine()
    {
        var quarantine = _paths.SettingsDatabasePath + QuarantineSuffix;

        try
        {
            // Nothing to move means the failure was CREATING the file rather than reading it
            // (a read-only directory, say). The two lead different places, so say which.
            if (!File.Exists(_paths.SettingsDatabasePath))
                return "there was no file to move aside, so the failure was creating it";

            File.Move(_paths.SettingsDatabasePath, quarantine, overwrite: true);
            return $"moved it to {quarantine}";
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            // Even the rename failed. The app still starts on defaults; every later write will
            // fail and log on its own. There is nothing further this layer can do.
            _logger.LogWarning(ex, "Could not move the unreadable settings database aside.");
            return "it could not be moved aside and stays where it is";
        }
    }

    /// <summary>
    /// What counts as "storage misbehaved" rather than "this code is wrong". Same list as
    /// <c>SqliteSendHistoryStore</c>; a bug in the SQL or the parameters should still surface.
    /// </summary>
    private static bool IsStorageFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException or FormatException;
}
