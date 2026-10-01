using DiSerial.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Manage recording database files.
internal static class RecordingDatabase
{
    public const int SchemaVersion = 1;

    // Ensure the recording schema.
    public static void Ensure(SqliteConnection connection, string path, ILogger? logger = null)
    {
        var found = ReadVersion(connection);

        if (found > SchemaVersion) throw new SchemaTooNewException(path, found, SchemaVersion);

        var isNewFile = !TableExists(connection, "batch")
                        && !TableExists(connection, "frame")
                        && !TableExists(connection, "send_history")
                        && !TableExists(connection, "shortcut")
                        && !TableExists(connection, "auto_answer");

        if (!isNewFile && found < SchemaVersion) BackUpIfAStepChangesAnything(path, found, logger);

        using var transaction = connection.BeginTransaction();

        Execute(connection, RecordingSchema.Tables);
        Execute(connection, RecordingSchema.SendHistoryDdl);
        Execute(connection, RecordingSchema.ShortcutDdl);
        Execute(connection, RecordingSchema.AutoAnswerDdl);

        if (!isNewFile)
        {
            for (var step = found + 1; step <= SchemaVersion; step++) ApplyStep(connection, step);
        }

        if (found != SchemaVersion) Execute(connection, $"PRAGMA user_version = {SchemaVersion};");

        transaction.Commit();

        if (!isNewFile && found < SchemaVersion)
        {
            logger?.LogInformation(
                "Stamped the recordings database at {Path}: schema {From} -> {To}.",
                path, found, SchemaVersion);
        }
    }

    // Check database compatibility.
    public static void CheckVersion(SqliteConnection connection, string path)
    {
        var found = ReadVersion(connection);
        if (found > SchemaVersion) throw new SchemaTooNewException(path, found, SchemaVersion);
    }

    // Apply a database migration step.
    private static void ApplyStep(SqliteConnection connection, int step)
    {
        switch (step)
        {
            case 1:
                break;

            default:
                throw new InvalidOperationException(
                    $"No migration is defined for recording schema step {step}.");
        }
    }

    // Check for a version-only step.
    private static bool IsStampOnly(int step) => step == 1;

    // Back up before schema changes.
    private static void BackUpIfAStepChangesAnything(string path, int found, ILogger? logger)
    {
        var changes = false;
        for (var step = found + 1; step <= SchemaVersion; step++) changes |= !IsStampOnly(step);

        if (!changes) return;

        var backup = $"{path}.v{found}.bak";

        try
        {
            File.Copy(path, backup, overwrite: true);
            logger?.LogInformation(
                "Copied the recordings database to {Backup} before migrating from schema {From}.",
                backup, found);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "Could not copy the recordings database to {Backup}; migrating without one.",
                backup);
        }
    }

    // Read the schema version.
    private static int ReadVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Check database table existence.
    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    // Execute a recording SQL command.
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
