using System.Diagnostics;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Replay;

// Emulate a serial port.
public sealed class ReplaySerialPort : ISerialPort
{
    private static readonly TimeSpan SpinThreshold = TimeSpan.FromMilliseconds(20);

    private readonly ReplayScript _script;
    private readonly ReplayOptions _options;
    private readonly IMonotonicClock _clock;
    private readonly IReadOnlyList<ReplayChunk> _chunks;
    private readonly ILogger _logger;

    private readonly Lock _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Initialize replay delivery.
    public ReplaySerialPort(
        string portName,
        SerialPortSettings settings,
        ReplayScript script,
        ReplayOptions options,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        PortName = portName;
        Settings = settings;
        _script = script;
        _options = options;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
        _chunks = ReplayChunkPlanner.Plan(script, options);
    }

    public string PortName { get; }

    public SerialPortSettings Settings { get; }

    public bool IsOpen { get; private set; }

    public event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    public event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    // Open the replay port.
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsOpen) return Task.CompletedTask;

            _cts?.Dispose();

            var cts = new CancellationTokenSource();
            _cts = cts;
            IsOpen = true;

            _loop = Task.Run(() => ReplayLoopAsync(cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }
    }

    // Close the replay port.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
        }

        if (cts is null)
        {
            lock (_gate) IsOpen = false;
            return;
        }

        await cts.CancelAsync();
        if (loop is not null)
        {
            try { await loop; }
            catch (OperationCanceledException) { }
        }

        lock (_gate)
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                _loop = null;
                IsOpen = false;
            }
        }

        cts.Dispose();
    }

    // Ignore replay port writes.
    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    // Return unknown line states.
    public SerialControlLines ReadControlLines() => SerialControlLines.Unknown;

    // Ignore replay output changes.
    public Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;


    // Release owned resources.
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        DataReceived = null;
        ErrorReceived = null;
    }

    // Deliver the replay scenario.
    private async Task ReplayLoopAsync(CancellationToken token)
    {
        var delivered = 0L;
        ReplayLog.LoopStarted(_logger, PortName, _script.Id, _chunks.Count, _script.Loop);

        try
        {
            do
            {
                foreach (var chunk in _chunks)
                {
                    await WaitAsync(chunk.DelayBefore, token);
                    if (token.IsCancellationRequested)
                    {
                        ReplayLog.LoopExited(_logger, PortName, "cancelled", delivered);
                        return;
                    }

                    if (chunk.Data.Length > 0)
                    {
                        DataReceived?.Invoke(
                            this, new SerialChunkReceivedEventArgs(chunk.Data, _clock.Now));
                        delivered++;
                    }

                    if (chunk.Fault is not { } fault) continue;

                    ErrorReceived?.Invoke(this, new SerialErrorEventArgs(
                        fault.Flags, fault.Message, fault.IsFatal, fault.Kind));

                    if (!fault.IsFatal) continue;

                    lock (_gate) IsOpen = false;
                    ReplayLog.LoopExited(_logger, PortName, "fatal fault", delivered);
                    return;
                }
            }
            while (_script.Loop && !token.IsCancellationRequested);

            ReplayLog.LoopExited(_logger, PortName, "script ended", delivered);
        }
        catch (OperationCanceledException)
        {
            ReplayLog.LoopExited(_logger, PortName, "cancelled", delivered);
        }
        catch (Exception ex)
        {
            lock (_gate) IsOpen = false;
            ReplayLog.LoopFaulted(_logger, ex, PortName, delivered, ex.GetType().Name);
        }
    }

    // Wait for a replay delivery time.
    private async Task WaitAsync(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        var start = Stopwatch.GetTimestamp();

        if (delay > SpinThreshold)
        {
            try
            {
                await Task.Delay(delay - SpinThreshold, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        while (Stopwatch.GetElapsedTime(start) < delay)
        {
            if (token.IsCancellationRequested) return;
            Thread.SpinWait(60);
        }
    }
}
