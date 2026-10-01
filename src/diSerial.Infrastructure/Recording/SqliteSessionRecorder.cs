using System.Threading.Channels;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Recording;

// Store captured session frames.
public sealed class SqliteSessionRecorder : ISessionRecorder
{
    private const int CommitBatchSize = 100;

    private static readonly TimeSpan CommitInterval = TimeSpan.FromMilliseconds(200);

    internal IMonotonicClock Clock { get; init; } = new DiSerial.Infrastructure.Time.MonotonicClock();

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly string _databasePath;
    private readonly ILogger _logger;

    private SqliteConnection? _connection;
    private Channel<SerialFrame>? _queue;
    private Task? _writerLoop;
    private CancellationTokenSource? _writerCts;

    private long _framesWritten;
    private long _bytesWritten;

    private long _framesDropped;

    // Initialize recording storage.
    public SqliteSessionRecorder(string databasePath, ILogger? logger = null)
    {
        _databasePath = databasePath;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsRecording { get; private set; }

    public long? CurrentBatchId { get; private set; }

    public long FramesWritten => Interlocked.Read(ref _framesWritten);

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    public long FramesDropped => Interlocked.Read(ref _framesDropped);

    public event EventHandler<RecordingFailedEventArgs>? Failed;

    // Start a recording batch.
    public async Task<long> StartAsync(RecordingBatchInfo info, CancellationToken cancellationToken = default)
    {
        if (IsRecording) return CurrentBatchId!.Value;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            _connection = new SqliteConnection($"Data Source={_databasePath}");
            await _connection.OpenAsync(cancellationToken);
            await ExecuteAsync(RecordingSchema.Pragmas, cancellationToken);

            RecordingDatabase.Ensure(_connection, _databasePath, _logger);

            CurrentBatchId = await InsertBatchAsync(info, cancellationToken);
        }
        catch
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            throw;
        }

        Interlocked.Exchange(ref _framesWritten, 0);
        Interlocked.Exchange(ref _bytesWritten, 0);

        Interlocked.Exchange(ref _framesDropped, 0);

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

    // Queue a frame for recording.
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

    // Finish the recording batch.
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRecording) return;

        IsRecording = false;

        _queue?.Writer.TryComplete();

        var drained = _writerLoop is null || await DrainAsync(_writerLoop);

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
            _connection = null;
        }

        _writerCts?.Dispose();
        _writerCts = null;
        _writerLoop = null;
        _queue = null;

        _logger.LogInformation(
            "Recording stopped: batch {BatchId}, {Frames} frames / {Bytes} bytes, {Dropped} dropped",
            CurrentBatchId, FramesWritten, BytesWritten, FramesDropped);
    }

    // Await queued recording writes.
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

    // Stop recording and release work.
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Failed = null;
    }


    // Write queued recording frames.
    private async Task WriterLoopAsync(CancellationToken token)
    {
        var buffer = new List<SerialFrame>(CommitBatchSize);
        var lastCommit = Clock.Now;

        try
        {
            var reader = _queue!.Reader;

            Task<bool>? waiting = null;

            while (true)
            {
                waiting ??= reader.WaitToReadAsync(token).AsTask();

                if (waiting == await Task.WhenAny(waiting, Task.Delay(CommitInterval, token)))
                {
                    if (!await waiting) break;
                    waiting = null;

                    while (reader.TryRead(out var frame))
                    {
                        buffer.Add(frame);

                        if (buffer.Count >= CommitBatchSize)
                        {
                            await CommitAsync(buffer, token);
                            lastCommit = Clock.Now;
                        }
                    }
                }

                if (buffer.Count > 0 && Clock.Now - lastCommit >= CommitInterval)
                {
                    await CommitAsync(buffer, token);
                    lastCommit = Clock.Now;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording write loop failed; the batch is incomplete.");
            Failed?.Invoke(this, new RecordingFailedEventArgs(ex));
            return;
        }

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

    // Commit pending recording writes.
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


    // Create the recorded batch row.
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

    // Mark a batch as finished.
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
            _logger.LogWarning(ex, "Marking batch {BatchId} as ended failed.", CurrentBatchId);
        }
    }

    // Execute a database command.
    private async Task ExecuteAsync(string sql, CancellationToken token)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(token);
    }
}
