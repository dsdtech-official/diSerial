using System.Threading.Channels;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// C-09 记录的落库实现。规格 docs/01-spec.md 4.10，机制 docs/02-architecture.md 第十二节。
///
/// <b>核心决定：只存原始字节，不存渲染结果。</b>
/// ascii / hex 是同一份字节的两种渲染，不是两份数据 —— 存两份会带来三个问题：
/// 同一事实两个来源、其中 ASCII 那份有损、将来改格式化规则会产生两种口径。
/// 渲染在**读**的时候用 <see cref="IFrameFormatter"/> 做。
///
/// <b>写入路径</b>：采集线程 → <see cref="Channel"/> → 独立写盘任务 → 批量事务。
/// ⚠️ <b>绝不能每帧一次事务</b> —— SQLite 每个事务一次 fsync，
/// 高帧率下会直接拖垮采集线程，而那条线程正是时间戳打点的地方（03-conventions 7.1）。
/// </summary>
public sealed class SqliteSessionRecorder : ISessionRecorder
{
    /// <summary>一个事务最多攒多少帧。</summary>
    private const int CommitBatchSize = 100;

    /// <summary>
    /// 攒不满也要提交的间隔上限 —— 低帧率下也要及时落盘，否则崩溃会丢掉最后一批。
    ///
    /// ⚠️ <b>这句话曾经不成立</b>（P1-51，2026-08-04 修）：判据原先<b>只在有帧到达时</b>求值，
    /// 于是队列排空之后线程阻塞在 <c>WaitToReadAsync</c> 上，攒着的那几帧
    /// <b>无限期留在内存里</b> —— 而<b>静默总线恰恰是这个常量声称要覆盖的场景</b>。
    /// 现在等待本身带 200ms 上限，空转一圈也会提交。
    /// </summary>
    private static readonly TimeSpan CommitInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 停止记录时，等写盘任务排空的上限（P1-51，2026-08-04 加）。
    ///
    /// <para>⛔ <b>在此之前没有上限</b>：磁盘满、数据库被锁、网络盘掉线时 SQLite 提交会一直阻塞，
    /// 而「停止记录」是 <c>await</c> 它的 —— <b>按钮就那样挂死</b>。</para>
    ///
    /// <para>⭐ <b>2 秒是照抄串口关闭的那个上限</b>（<c>SystemIoSerialPort</c>）——
    /// 同一个问题（等一个可能永远不返回的 I/O）此前在本项目里有两种处理，
    /// 那本身就是缺陷的一半。</para>
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly string _databasePath;
    private readonly ILogger _logger;

    private SqliteConnection? _connection;
    private Channel<SerialFrame>? _queue;
    private Task? _writerLoop;
    private CancellationTokenSource? _writerCts;

    private long _framesWritten;
    private long _bytesWritten;

    /// <summary>
    /// Frames handed to <see cref="WriteAsync"/> that could not be queued (P2-37).
    ///
    /// <para>⛔ <b>Before this existed the drop was completely silent</b>:
    /// <c>_queue?.Writer.TryWrite(frame)</c> discarded a <c>bool</c>, the method returned
    /// <c>Task.CompletedTask</c>, and the contract offers the caller no return value and no
    /// event. For a recorder, "lost it and said nothing" is the one outcome that must not be
    /// possible — the user's whole reason to press Record is to have the data afterwards.</para>
    /// </summary>
    private long _framesDropped;

    public SqliteSessionRecorder(string databasePath, ILogger? logger = null)
    {
        _databasePath = databasePath;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsRecording { get; private set; }

    public long? CurrentBatchId { get; private set; }

    public long FramesWritten => Interlocked.Read(ref _framesWritten);

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    /// <summary>
    /// How many frames this batch handed over and lost (P2-37). See <see cref="WriteAsync"/> for
    /// the one window in which that is possible, and why it is not the same thing as a frame
    /// arriving while no recording is running.
    /// </summary>
    public long FramesDropped => Interlocked.Read(ref _framesDropped);

    public event EventHandler<RecordingFailedEventArgs>? Failed;

    /// <summary>
    /// Opens the database and starts a batch.
    ///
    /// <para>⛔ <b>The early return is only sound if somebody calls <see cref="StopAsync"/> after
    /// <see cref="Failed"/></b> (P2-89). Before that nobody did, so a batch whose writer loop had
    /// died left this object with <c>IsRecording == true</c> forever: pressing Record again
    /// returned the <b>old</b> batch id without doing anything, and the UI showed "recording"
    /// while every frame went into a queue nobody was reading — not one byte reached the disk,
    /// with no log line and no banner.</para>
    ///
    /// <para>⚠️ <b>That makes this an obligation on the subscriber, not a guarantee of this
    /// class</b> — the user chose that fix (2026-08-08) over having the writer loop clear the
    /// flag itself. <c>SessionViewModel.OnRecordingFailed</c> is the one that owes it, and
    /// <c>RecorderRestartsAfterWriteFailureTests</c> is what holds both ends together.</para>
    ///
    /// <para>⚠️ <b>Everything that can throw is inside the try</b> (P2-89, second half): a
    /// failure here used to leave a half-open <see cref="SqliteConnection"/> in the field with
    /// nobody to dispose it, and the caller's retry overwrote the reference. The channel, the
    /// CTS and the loop are created afterwards precisely because none of them throw.</para>
    /// </summary>
    public async Task<long> StartAsync(RecordingBatchInfo info, CancellationToken cancellationToken = default)
    {
        if (IsRecording) return CurrentBatchId!.Value;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            _connection = new SqliteConnection($"Data Source={_databasePath}");
            await _connection.OpenAsync(cancellationToken);
            await ExecuteAsync(RecordingSchema.Ddl, cancellationToken);

            CurrentBatchId = await InsertBatchAsync(info, cancellationToken);
        }
        catch
        {
            // ⭐ Give the connection back before the caller retries. The exception still goes
            // up -- the caller is the one that reports it (01-spec 4.7 path 6), and swallowing
            // it here would turn a failed start into a silent one.
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            throw;
        }

        Interlocked.Exchange(ref _framesWritten, 0);
        Interlocked.Exchange(ref _bytesWritten, 0);

        // Per batch, like the other two -- otherwise a drop from a previous batch would keep
        // being reported against every batch after it.
        Interlocked.Exchange(ref _framesDropped, 0);

        // 无界队列：采集侧绝不能因为写盘慢而阻塞。写盘跟不上时内存会涨，
        // 但那是可观测的，而阻塞采集线程会直接破坏时间戳。
        _queue = Channel.CreateUnbounded<SerialFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _writerCts = new CancellationTokenSource();
        _writerLoop = Task.Run(() => WriterLoopAsync(_writerCts.Token), CancellationToken.None);

        IsRecording = true;
        _logger.LogInformation(
            "Recording started: batch {BatchId} into {Database}", CurrentBatchId, _databasePath);

        return CurrentBatchId.Value;
    }

    /// <summary>由采集线程调用 —— 只入队，立即返回。</summary>
    /// <summary>
    /// ⚠️ <b>A frame that cannot be queued is counted and logged, never dropped in silence</b>
    /// (P2-37). The contract gives the caller nothing to check — no return value, no event — so
    /// if this method says nothing, nothing anywhere ever will.
    ///
    /// <para>⛔ <b>The two ways a frame does not reach the queue are not the same thing</b>, and
    /// merging them would make the count useless:</para>
    ///
    /// <list type="bullet">
    ///   <item><b>Not recording at all</b> (<c>_queue</c> is null) — <b>expected</b>. The capture
    ///         thread can still be in flight for a moment after Stop; that window is what P2-29
    ///         is about. Nothing is lost, because nothing was meant to be kept.</item>
    ///   <item><b>Recording, but the queue refused it</b> — <b>a real loss</b>. The channel is
    ///         unbounded, so the only way <c>TryWrite</c> fails is that the writer side has been
    ///         completed: <c>StopAsync</c> ran between the caller's recording check and this
    ///         call. One frame, at the very end of a batch — which is exactly the frame the user
    ///         was watching when they decided to stop (the same argument as P2-29).</item>
    /// </list>
    ///
    /// <para>⚠️ <b>It deliberately does not raise <c>Failed</c>.</b> That event means "recording
    /// has stopped and you should know why", and the UI answers it by turning the button back
    /// and showing a red banner. Firing it because the user pressed Stop would report a failure
    /// that did not happen — the [第 1 层] "the tool is lying" defect, in the other direction.
    /// The count goes into the stop summary instead, where it is true and useful.</para>
    /// </summary>
    public Task WriteAsync(SerialFrame frame, CancellationToken cancellationToken = default)
    {
        if (_queue is not { } queue) return Task.CompletedTask;

        if (queue.Writer.TryWrite(frame)) return Task.CompletedTask;

        var dropped = Interlocked.Increment(ref _framesDropped);

        _logger.LogWarning(
            "Recording dropped frame {Sequence}: the queue was already closed "
            + "({Dropped} dropped in this batch).",
            frame.Sequence, dropped);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRecording) return;

        IsRecording = false;

        // 先关写入端，让写盘任务把队列里剩下的排空之后自然退出 —— 不取消，否则会丢最后一批。
        _queue?.Writer.TryComplete();

        // ⭐ P1-51: 排空有上限了。正常情况下这一等是毫秒级的；
        // 磁盘满 / 库被锁 / 网络盘掉线时 SQLite 提交会一直阻塞，而在此之前这里是无限等待 ——
        // 「停止记录」按钮就那样挂死，用户唯一的出路是杀进程。
        var drained = _writerLoop is null || await DrainAsync(_writerLoop);

        // ⛔ 超时之后**不碰连接**：那条任务可能正卡在它上面的一次提交里，
        // 而 SqliteConnection 不是线程安全的 —— 一边释放一边被用，比挂住更糟。
        // ⚠️ 代价是这一次的连接与那条任务都留着不回收。**明知接受**：
        // 能走到这里说明库已经写不动了，而让界面活下来是更要紧的那一半。
        if (drained)
        {
            await MarkBatchEndedAsync(cancellationToken);

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        else
        {
            // 交出所有权而不是释放它。
            _connection = null;
        }

        _writerCts?.Dispose();
        _writerCts = null;
        _writerLoop = null;
        _queue = null;

        // ⚠️ Dropped is in the summary on purpose, even when it is 0 (P2-37): a field that only
        // appears when something went wrong is a field nobody knows to look for. Reading
        // "0 dropped" is what makes the non-zero case mean something.
        _logger.LogInformation(
            "Recording stopped: batch {BatchId}, {Frames} frames / {Bytes} bytes, {Dropped} dropped",
            CurrentBatchId, FramesWritten, BytesWritten, FramesDropped);
    }

    /// <summary>
    /// 等写盘任务排空，最多 <see cref="DrainTimeout"/>。返回它是否真的排空了。
    ///
    /// <para>⭐ <b>超时后取消，但<u>不再等</u></b>：它可能卡在一次 SQLite 提交里，
    /// 而那正是我们不肯再等的东西 —— 再 <c>await</c> 一次就把刚拿回来的响应性又还回去了。
    /// <c>_writerCts</c> 在此之前<b>从来没有被取消过</b>（P1-51 也点了这一处，它当时是死代码）；
    /// 现在它有了唯一的用途：给那条任务一条退出的路，走不走得掉不由我们决定。</para>
    ///
    /// <para>⚠️ <b>记 Error 而不是 Warning</b>：它一次会话最多发生一次，
    /// 且意味着**这一批记录很可能不完整** —— 用户有权在日志里看见这件事。
    /// 判据见 01-spec 4.7 共有第 1 条：会重复触发的才压 Debug。</para>
    /// </summary>
    private async Task<bool> DrainAsync(Task writerLoop)
    {
        if (writerLoop == await Task.WhenAny(writerLoop, Task.Delay(DrainTimeout)))
        {
            try { await writerLoop; }
            catch (OperationCanceledException) { }
            return true;
        }

        _writerCts?.Cancel();

        _logger.LogError(
            "The recording writer did not drain within {Timeout} ms; batch {BatchId} may be "
            + "incomplete and its connection is left to the stuck task.",
            DrainTimeout.TotalMilliseconds, CurrentBatchId);

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Failed = null;
    }

    // ---- 写盘任务 ----

    private async Task WriterLoopAsync(CancellationToken token)
    {
        var buffer = new List<SerialFrame>(CommitBatchSize);
        var lastCommit = DateTimeOffset.UtcNow;

        try
        {
            var reader = _queue!.Reader;

            // ⭐ P1-51: 等待本身带上限，所以「攒不满也要提交」在**静默总线**上也成立。
            //
            // 原先是 `while (await reader.WaitToReadAsync(token))` —— 于是间隔判据
            // **只在有新帧到达时**才求得到值。队列一排空，线程就阻塞在这里，
            // 攒着的那几帧无限期留在内存中，崩溃或强杀就没了。
            // ⛔ 而**静默总线恰恰是 CommitInterval 那句注释声称要覆盖的场景** ——
            // 它防的是「低帧率下丢最后一批」，而低帧率正是它失效的条件。
            //
            // ⚠️ `waiting` 必须跨轮保留：`WaitToReadAsync` 每调一次就是一个新的等待者，
            // 空转一圈就重新调一次会把上一次挂着的那个丢掉。
            Task<bool>? waiting = null;

            while (true)
            {
                waiting ??= reader.WaitToReadAsync(token).AsTask();

                if (waiting == await Task.WhenAny(waiting, Task.Delay(CommitInterval, token)))
                {
                    if (!await waiting) break;      // 写入端已关闭，去做最后的冲洗
                    waiting = null;

                    while (reader.TryRead(out var frame))
                    {
                        buffer.Add(frame);

                        if (buffer.Count >= CommitBatchSize)
                        {
                            await CommitAsync(buffer, token);
                            lastCommit = DateTimeOffset.UtcNow;
                        }
                    }
                }

                // 两条路都到这里：读到了一批，或者空转满了一个间隔。
                // ⭐ 判据只有一个 —— 手上有东西且离上次提交够久了。
                if (buffer.Count > 0 && DateTimeOffset.UtcNow - lastCommit >= CommitInterval)
                {
                    await CommitAsync(buffer, token);
                    lastCommit = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 取消是正常控制流（01-spec 4.7 三个出口之三）。
        }
        catch (Exception ex)
        {
            // 写盘失败：磁盘满、数据库损坏、路径失效。
            // 抛出去没人接得住（采集线程早已走远），所以走事件 ——
            // 调用方据此停止记录并提示用户（01-spec 4.7 第 6 条路径）。
            _logger.LogError(ex, "Recording write loop failed; the batch is incomplete.");
            Failed?.Invoke(this, new RecordingFailedEventArgs(ex));
            return;
        }

        // 队列关闭后把剩下的冲干净 —— 这一步不能被取消，否则丢最后一批。
        if (buffer.Count > 0)
        {
            try
            {
                await CommitAsync(buffer, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flushing the final recording batch failed.");
                Failed?.Invoke(this, new RecordingFailedEventArgs(ex));
            }
        }
    }

    private async Task CommitAsync(List<SerialFrame> buffer, CancellationToken token)
    {
        if (buffer.Count == 0) return;

        await using var tx = await _connection!.BeginTransactionAsync(token);
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = RecordingSchema.InsertFrame;

        var pBatch = cmd.CreateParameter(); pBatch.ParameterName = "$batch"; cmd.Parameters.Add(pBatch);
        var pSeq = cmd.CreateParameter(); pSeq.ParameterName = "$seq"; cmd.Parameters.Add(pSeq);
        var pTs = cmd.CreateParameter(); pTs.ParameterName = "$ts"; cmd.Parameters.Add(pTs);
        var pElapsed = cmd.CreateParameter(); pElapsed.ParameterName = "$elapsed"; cmd.Parameters.Add(pElapsed);
        var pDelta = cmd.CreateParameter(); pDelta.ParameterName = "$delta"; cmd.Parameters.Add(pDelta);
        var pChannel = cmd.CreateParameter(); pChannel.ParameterName = "$channel"; cmd.Parameters.Add(pChannel);
        var pDir = cmd.CreateParameter(); pDir.ParameterName = "$dir"; cmd.Parameters.Add(pDir);
        var pFlags = cmd.CreateParameter(); pFlags.ParameterName = "$flags"; cmd.Parameters.Add(pFlags);
        var pData = cmd.CreateParameter(); pData.ParameterName = "$data"; cmd.Parameters.Add(pData);

        long frames = 0, bytes = 0;
        foreach (var f in buffer)
        {
            pBatch.Value = CurrentBatchId!.Value;
            pSeq.Value = f.Sequence;
            pTs.Value = f.Timestamp.UtcDateTime.ToString("O");
            pElapsed.Value = f.Elapsed.TotalMilliseconds;
            pDelta.Value = f.Delta.HasValue ? f.Delta.Value.TotalMilliseconds : DBNull.Value;
            pChannel.Value = (int)f.Channel;
            pDir.Value = (int)f.Direction;
            pFlags.Value = (int)f.Flags;
            pData.Value = f.Data.ToArray();

            await cmd.ExecuteNonQueryAsync(token);
            frames++;
            bytes += f.Length;
        }

        await tx.CommitAsync(token);
        buffer.Clear();

        Interlocked.Add(ref _framesWritten, frames);
        Interlocked.Add(ref _bytesWritten, bytes);
    }

    // ---- 批次 ----

    private async Task<long> InsertBatchAsync(RecordingBatchInfo info, CancellationToken token)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = RecordingSchema.InsertBatch;
        cmd.Parameters.AddWithValue("$kind", info.SessionKind.ToString());
        cmd.Parameters.AddWithValue("$portA", info.PortA);
        cmd.Parameters.AddWithValue("$portB", (object?)info.PortB ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aliasA", (object?)info.AliasA ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aliasB", (object?)info.AliasB ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$baud", info.Settings.BaudRate);
        cmd.Parameters.AddWithValue("$dataBits", info.Settings.DataBits);
        cmd.Parameters.AddWithValue("$parity", info.Settings.Parity.ToString());
        cmd.Parameters.AddWithValue("$stopBits", info.Settings.StopBits.ToString());
        cmd.Parameters.AddWithValue("$startedAt", DateTimeOffset.UtcNow.ToString("O"));

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(token));
    }

    private async Task MarkBatchEndedAsync(CancellationToken token)
    {
        if (_connection is null || CurrentBatchId is null) return;

        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = RecordingSchema.MarkBatchEnded;
            cmd.Parameters.AddWithValue("$id", CurrentBatchId.Value);
            cmd.Parameters.AddWithValue("$endedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(token);
        }
        catch (Exception ex)
        {
            // 收尾失败不影响已落库的帧，但要留痕 —— 否则 ended_at 为 null 会被误读成「还在记录」。
            _logger.LogWarning(ex, "Marking batch {BatchId} as ended failed.", CurrentBatchId);
        }
    }

    private async Task ExecuteAsync(string sql, CancellationToken token)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(token);
    }
}
