using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Persist automatic reply rules.
public sealed class SqliteAutoAnswerStore(IAppPaths paths, ILoggerFactory loggerFactory)
    : IAutoAnswerStore
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<SqliteAutoAnswerStore>();

    // Load saved reply rules.
    public IReadOnlyList<AutoAnswerRule> Load()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.LoadAutoAnswerRules;

        var rules = new List<AutoAnswerRule>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);

            rules.Add(new AutoAnswerRule(
                id,
                reader.GetString(1),
                ReadFormat(reader.GetString(2), id, "trigger_format"),
                reader.GetString(3),
                ReadFormat(reader.GetString(4), id, "response_format"),
                reader.GetString(5),
                ReadTerminator(reader.GetString(6), id),
                reader.GetInt64(7)));
        }

        return rules;
    }

    // Save an automatic reply rule.
    public AutoAnswerSaveResult Add(
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        var refusal = Validate(name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        if (refusal is { } reason) return reason;

        using var connection = Open();

        if (Count(connection) >= AutoAnswerLimits.MaxCount)
        {
            _logger.LogDebug(
                "Auto-answer rule not added: the store already holds {Max} rules.",
                AutoAnswerLimits.MaxCount);
            return AutoAnswerSaveResult.Full;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.InsertAutoAnswerRule;
        Bind(cmd, name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        cmd.Parameters.AddWithValue("$ordinal", NextOrdinal(connection));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return AutoAnswerSaveResult.Saved;
    }

    // Update a saved reply rule.
    public AutoAnswerSaveResult Update(
        long id,
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        var refusal = Validate(name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        if (refusal is { } reason) return reason;

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.UpdateAutoAnswerRule;
        Bind(cmd, name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        cmd.Parameters.AddWithValue("$id", id);

        return cmd.ExecuteNonQuery() > 0
            ? AutoAnswerSaveResult.Saved
            : AutoAnswerSaveResult.NotFound;
    }

    // Delete a saved reply rule.
    public void Delete(long id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.DeleteAutoAnswerRule;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // Move a reply rule to the front.
    public void MoveToFront(long id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.AutoAnswerToFront;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // Move a reply rule up one place.
    public void MoveForwardOne(long id) => Swap(id, -1);

    // Move a reply rule down one slot.
    public void MoveBackOne(long id) => Swap(id, +1);

    // Swap two reply rule positions.
    private void Swap(long id, int step)
    {
        var rules = Load();
        var index = -1;
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].Id == id) { index = i; break; }
        }

        var target = index + step;
        if (index < 0 || target < 0 || target >= rules.Count) return;

        var self = rules[index];
        var other = rules[target];

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        SetOrdinal(connection, self.Id, other.Ordinal);
        SetOrdinal(connection, other.Id, self.Ordinal);

        if (other.Ordinal == self.Ordinal)
        {
            SetOrdinal(connection, self.Id, self.Ordinal + step);
        }

        transaction.Commit();
    }

    // Validate reply rule values.
    private AutoAnswerSaveResult? Validate(
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        if (name.Trim().Length > AutoAnswerLimits.MaxNameLength)
        {
            _logger.LogDebug("Auto-answer rule refused: the name is longer than the limit.");
            return AutoAnswerSaveResult.InvalidName;
        }

        if (!Encodes(triggerFormat, triggerText, LineEnding.None))
        {
            _logger.LogDebug("Auto-answer rule refused: the trigger does not encode.");
            return AutoAnswerSaveResult.InvalidTrigger;
        }

        if (!Encodes(responseFormat, responseText, terminator))
        {
            _logger.LogDebug("Auto-answer rule refused: the response does not encode.");
            return AutoAnswerSaveResult.InvalidResponse;
        }

        return null;
    }

    // Check payload encoding validity.
    private static bool Encodes(PayloadFormat format, string text, LineEnding terminator) =>
        PayloadParser.TryParse(text, format == PayloadFormat.Hex, terminator, out var bytes)
        && bytes.Length > 0
        && bytes.Length <= AutoAnswerLimits.MaxPatternBytes;

    // Bind reply rule SQL parameters.
    private static void Bind(
        SqliteCommand cmd,
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$triggerFormat", triggerFormat.ToString());
        cmd.Parameters.AddWithValue("$triggerText", triggerText);
        cmd.Parameters.AddWithValue("$responseFormat", responseFormat.ToString());
        cmd.Parameters.AddWithValue("$responseText", responseText);
        cmd.Parameters.AddWithValue("$terminator", terminator.ToString());
    }

    // Parse a stored payload format.
    private PayloadFormat ReadFormat(string raw, long id, string column)
    {
        if (Enum.TryParse<PayloadFormat>(raw, ignoreCase: true, out var format)
            && Enum.IsDefined(format))
        {
            return format;
        }

        _logger.LogWarning(
            "Auto-answer rule {Id} has an unreadable {Column} ({Raw}); reading it as Ascii.",
            id, column, raw);
        return PayloadFormat.Ascii;
    }

    // Parse a stored line terminator.
    private LineEnding ReadTerminator(string raw, long id)
    {
        if (Enum.TryParse<LineEnding>(raw, ignoreCase: true, out var terminator)
            && Enum.IsDefined(terminator))
        {
            return terminator;
        }

        _logger.LogWarning(
            "Auto-answer rule {Id} has an unreadable terminator ({Raw}); reading it as None.",
            id, raw);
        return LineEnding.None;
    }

    // Set a stored item's position.
    private static void SetOrdinal(SqliteConnection connection, long id, long ordinal)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE auto_answer SET ordinal = $ordinal WHERE id = $id;";
        cmd.Parameters.AddWithValue("$ordinal", ordinal);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // Count stored reply rules.
    private static int Count(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.CountAutoAnswerRules;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Get the next stored position.
    private static long NextOrdinal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = RecordingSchema.NextAutoAnswerOrdinal;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // Open the reply rule database.
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={paths.RecordingDatabasePath}");
        connection.Open();
        RecordingDatabase.Ensure(connection, paths.RecordingDatabasePath, _logger);
        return connection;
    }
}
