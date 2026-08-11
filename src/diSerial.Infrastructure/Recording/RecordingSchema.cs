namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// Table layout and statements for the recording database. Design notes are in
/// docs/02-architecture.md section 12.
///
/// <para>⚠️ <b>The schema has to be right the first time</b> — V1.1's batch query and
/// management is meant to add UI, not to change tables.</para>
///
/// <para>⛔ <b>Which columns V1.0 leaves idle — re-checked against the code 2026-08-11.</b>
/// This paragraph used to read "<c>batch.note</c> / <c>batch.ended_at</c> /
/// <c>frame.direction</c> are reserved for V1.1; V1.0 writes them but does not read them
/// yet". One sentence, three subjects, and it was wrong about two of them — in opposite
/// directions:</para>
///
/// <list type="bullet">
///   <item><c>batch.note</c> — <b>neither written nor read.</b> It is absent from
///     <see cref="InsertBatch"/> and nothing updates it, so "V1.0 writes them" was never
///     true of this one.</item>
///   <item><c>batch.ended_at</c> — <b>written, not read.</b> The recorder issues
///     <see cref="MarkBatchEnded"/>; no SELECT asks for it. The old sentence was right
///     about this column, and only this one.</item>
///   <item><c>frame.direction</c> — <b>written AND read: it is in use.</b>
///     <c>SqliteRecordingReader</c> selects it and materialises it into
///     <c>SerialFrame.Direction</c> — that is where the Direction column of an exported
///     batch comes from.</item>
/// </list>
///
/// <para>⭐ <b>The shape is worth more than the three facts.</b> A sentence that lists
/// several columns ages at the rate of the fastest-moving one, and whoever starts reading
/// one of those columns has no reason to open this file. So: when a column changes status,
/// say it where that column is declared — <b>do not extend this list</b>.</para>
/// </summary>
internal static class RecordingSchema
{
    /// <summary>
    /// 建表 + 打开 WAL。
    ///
    /// <b>WAL 有两个理由</b>：读写不互斥（V1.1 边记边查）；
    /// 且崩溃后**不会得到半行** —— 而文本文件会截断在半行。这一条比文本方案强。
    ///
    /// <c>synchronous = NORMAL</c>：WAL 模式下这一档不会因为进程崩溃丢已提交的事务
    /// （只有掉电才可能），换来的是写入快得多。对本用途是合适的取舍。
    /// </summary>
    public const string Ddl = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous  = NORMAL;
        PRAGMA foreign_keys = ON;

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

    /// <summary>
    /// Send history (T-03a persistence, user decision 2026-08-02). Lives in the same file as
    /// the recordings on purpose — one database, and adding a table is purely additive:
    /// every statement is <c>IF NOT EXISTS</c>, so an existing recordings.db picks it up on
    /// the next open with no migration step. <b>Nothing in <see cref="Ddl"/> is touched</b>,
    /// which matters because this schema has no version column and no migration mechanism.
    ///
    /// ⚠️ <b>This table must survive anything that clears captured data.</b> It is user
    /// configuration sharing a file with disposable capture output. When V1.1 adds batch
    /// management (00-STATUS P2-19), "delete batches" and "reclaim space" must not reach it —
    /// note that <c>frame</c> cascades from <c>batch</c> while this table is deliberately
    /// unrelated to both.
    ///
    /// ⚠️ <b>UNIQUE is (text, is_hex), not text.</b> The same characters are different bytes
    /// in the two modes, and picking an entry restores its mode, so they are two commands.
    ///
    /// <c>weight</c> is written as 0 and never read yet — it is the slot for a future ranking
    /// (00-STATUS: step 3 of the history plan), kept here so the data exists when that gets
    /// decided rather than starting from zero then.
    /// </summary>
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

    /// <summary>
    /// Insert, or bump an existing row. <c>first_used_at</c> is deliberately left alone on
    /// conflict — it answers "since when has this been in use", which a bump would destroy.
    /// </summary>
    public const string UpsertSendHistory = """
        INSERT INTO send_history (text, is_hex, line_ending, use_count, first_used_at, last_used_at)
        VALUES ($text, $isHex, $lineEnding, 1, $now, $now)
        ON CONFLICT (text, is_hex) DO UPDATE SET
            use_count    = use_count + 1,
            last_used_at = $now,
            line_ending  = $lineEnding;
        """;

    /// <summary>
    /// Trims to the cap, least-recently-used first.
    ///
    /// ⚠️ <b>Known, accepted cost</b>: eviction ignores <c>use_count</c>, so a command used
    /// often but not lately is dropped along with its counter. That biases the data a future
    /// ranking would learn from. Accepted at a cap of 100; revisit if the cap grows.
    /// </summary>
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

    public const string DeleteSendHistory =
        "DELETE FROM send_history WHERE text = $text AND is_hex = $isHex;";

    public const string CountSendHistory = "SELECT COUNT(*) FROM send_history;";

    /// <summary>
    /// ⚠️ Scoped to this table only. The recordings live in the same file and are none of this
    /// feature's business — see the warning on <see cref="SendHistoryDdl"/>.
    /// </summary>
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
}
