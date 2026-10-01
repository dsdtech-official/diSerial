namespace DiSerial.Infrastructure.Recording;

// Manage recording schemas.
internal static class RecordingSchema
{
    public const string Pragmas = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous  = NORMAL;
        PRAGMA foreign_keys = ON;
        """;

    public const string Tables = """
        CREATE TABLE IF NOT EXISTS batch (
            id            INTEGER PRIMARY KEY,
            session_kind  TEXT    NOT NULL,
            port_a        TEXT    NOT NULL,
            port_b        TEXT,
            alias_a       TEXT,
            alias_b       TEXT,
            baud_rate     INTEGER NOT NULL,
            data_bits     INTEGER NOT NULL,
            parity        TEXT    NOT NULL,
            stop_bits     TEXT    NOT NULL,
            started_at    TEXT    NOT NULL,
            ended_at      TEXT,
            note          TEXT
        );

        CREATE TABLE IF NOT EXISTS frame (
            id            INTEGER PRIMARY KEY,
            batch_id      INTEGER NOT NULL REFERENCES batch(id) ON DELETE CASCADE,
            seq           INTEGER NOT NULL,
            timestamp_utc TEXT    NOT NULL,
            elapsed_ms    REAL    NOT NULL,
            delta_ms      REAL,
            channel       INTEGER NOT NULL,
            direction     INTEGER NOT NULL,
            flags         INTEGER NOT NULL,
            data          BLOB    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_frame_batch_time ON frame(batch_id, timestamp_utc);
        """;

    public const string SendHistoryDdl = """
        CREATE TABLE IF NOT EXISTS send_history (
            id            INTEGER PRIMARY KEY,
            text          TEXT    NOT NULL,
            is_hex        INTEGER NOT NULL,
            line_ending   TEXT,
            use_count     INTEGER NOT NULL DEFAULT 1,
            first_used_at TEXT    NOT NULL,
            last_used_at  TEXT    NOT NULL,
            weight        REAL    NOT NULL DEFAULT 0,
            UNIQUE (text, is_hex)
        );

        CREATE INDEX IF NOT EXISTS ix_send_history_last_used ON send_history(last_used_at DESC);
        """;

    public const string UpsertSendHistory = """
        INSERT INTO send_history (text, is_hex, line_ending, use_count, first_used_at, last_used_at)
        VALUES ($text, $isHex, $lineEnding, 1, $now, $now)
        ON CONFLICT (text, is_hex) DO UPDATE SET
            use_count    = use_count + 1,
            last_used_at = $now,
            line_ending  = $lineEnding;
        """;

    public const string TrimSendHistory = """
        DELETE FROM send_history
        WHERE id NOT IN (
            SELECT id FROM send_history ORDER BY last_used_at DESC, id DESC LIMIT $cap
        );
        """;

    public const string LoadSendHistory = """
        SELECT text, is_hex, line_ending, use_count, last_used_at
        FROM send_history
        ORDER BY last_used_at DESC, id DESC
        LIMIT $limit;
        """;

    public const string LatestSendHistoryUse = "SELECT MAX(last_used_at) FROM send_history;";

    public const string DeleteSendHistory =
        "DELETE FROM send_history WHERE text = $text AND is_hex = $isHex;";

    public const string CountSendHistory = "SELECT COUNT(*) FROM send_history;";

    public const string ClearSendHistory = "DELETE FROM send_history;";

    public const string InsertBatch = """
        INSERT INTO batch
            (session_kind, port_a, port_b, alias_a, alias_b,
             baud_rate, data_bits, parity, stop_bits, started_at)
        VALUES
            ($kind, $portA, $portB, $aliasA, $aliasB,
             $baud, $dataBits, $parity, $stopBits, $startedAt);
        SELECT last_insert_rowid();
        """;

    public const string InsertFrame = """
        INSERT INTO frame
            (batch_id, seq, timestamp_utc, elapsed_ms, delta_ms, channel, direction, flags, data)
        VALUES
            ($batch, $seq, $ts, $elapsed, $delta, $channel, $dir, $flags, $data);
        """;

    public const string MarkBatchEnded = "UPDATE batch SET ended_at = $endedAt WHERE id = $id;";

    public const string ShortcutDdl = """
        CREATE TABLE IF NOT EXISTS shortcut (
            id         INTEGER PRIMARY KEY,
            name       TEXT    NOT NULL,
            payload    BLOB    NOT NULL,
            ordinal    INTEGER NOT NULL,
            created_at TEXT    NOT NULL,
            meta       TEXT,
            flags      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (name)
        );

        CREATE INDEX IF NOT EXISTS ix_shortcut_ordinal ON shortcut(ordinal);
        """;

    public const string AutoAnswerDdl = """
        CREATE TABLE IF NOT EXISTS auto_answer (
            id               INTEGER PRIMARY KEY,
            name             TEXT    NOT NULL,
            trigger_format   TEXT    NOT NULL,
            trigger_text     TEXT    NOT NULL,
            response_format  TEXT    NOT NULL,
            response_text    TEXT    NOT NULL,
            terminator       TEXT    NOT NULL,
            ordinal          INTEGER NOT NULL,
            created_at       TEXT    NOT NULL,
            meta             TEXT,
            flags            INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS ix_auto_answer_ordinal ON auto_answer(ordinal);
        """;

    public const string LoadAutoAnswerRules = """
        SELECT id, name, trigger_format, trigger_text,
               response_format, response_text, terminator, ordinal
          FROM auto_answer
         ORDER BY ordinal, id;
        """;

    public const string CountAutoAnswerRules = "SELECT COUNT(*) FROM auto_answer;";

    public const string NextAutoAnswerOrdinal =
        "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM auto_answer;";

    public const string InsertAutoAnswerRule = """
        INSERT INTO auto_answer
          (name, trigger_format, trigger_text, response_format, response_text,
           terminator, ordinal, created_at)
        VALUES ($name, $triggerFormat, $triggerText, $responseFormat, $responseText,
                $terminator, $ordinal, $now);
        """;

    public const string UpdateAutoAnswerRule = """
        UPDATE auto_answer
           SET name = $name,
               trigger_format = $triggerFormat,
               trigger_text = $triggerText,
               response_format = $responseFormat,
               response_text = $responseText,
               terminator = $terminator
         WHERE id = $id;
        """;

    public const string DeleteAutoAnswerRule = "DELETE FROM auto_answer WHERE id = $id;";

    public const string AutoAnswerToFront = """
        UPDATE auto_answer
           SET ordinal = (SELECT MIN(ordinal) FROM auto_answer) - 1
         WHERE id = $id;
        """;


    public const string LoadShortcuts =
        "SELECT id, name, payload, ordinal FROM shortcut ORDER BY ordinal, id;";

    public const string CountShortcuts = "SELECT COUNT(*) FROM shortcut;";

    public const string NextShortcutOrdinal =
        "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM shortcut;";

    public const string InsertShortcut = """
        INSERT INTO shortcut (name, payload, ordinal, created_at)
        VALUES ($name, $payload, $ordinal, $now);
        """;

    public const string RenameShortcut = "UPDATE shortcut SET name = $name WHERE id = $id;";

    public const string DeleteShortcut = "DELETE FROM shortcut WHERE id = $id;";

    public const string ShortcutToFront = """
        UPDATE shortcut
           SET ordinal = (SELECT MIN(ordinal) FROM shortcut) - 1
         WHERE id = $id;
        """;
}
