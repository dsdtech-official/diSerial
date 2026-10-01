using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Sessions;

// Capture two ports together.
public sealed class MonitorCaptureSession : ICaptureSession
{
    private static readonly TimeSpan MinFlushPeriod = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan MaxFlushPeriod = TimeSpan.FromMilliseconds(50);

    private readonly ISerialPort _portA;
    private readonly ISerialPort _portB;
    private readonly Func<IFrameSplitter> _newSplitter;

    private IFrameSplitter _splitterA;
    private IFrameSplitter _splitterB;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _logger;

    private readonly Lock _sync = new();

    private CancellationTokenSource? _flushCts;
    private Task? _flushLoop;

    private long _sequence;

    private DateTimeOffset? _previousFrameEndedAt;

    // Initialize capture channels.
    public MonitorCaptureSession(
        ISerialPort portA,
        ISerialPort portB,
        Func<IFrameSplitter> newSplitter,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        _portA = portA;
        _portB = portB;
        _newSplitter = newSplitter;

        _splitterA = newSplitter();
        _splitterB = newSplitter();
        _clock = clock;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        _portA.DataReceived += OnDataReceivedA;
        _portB.DataReceived += OnDataReceivedB;
        _portA.ErrorReceived += OnErrorReceivedA;
        _portB.ErrorReceived += OnErrorReceivedB;
    }

    public SessionKind Kind => SessionKind.Monitor;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public DateTimeOffset? StartedAt { get; private set; }

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    // Start dual-port capture.
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected) return;

        SetState(ConnectionState.Connecting);

        await OpenOrThrowAsync(_portA, ChannelId.A, rollback: null, cancellationToken);
        await OpenOrThrowAsync(_portB, ChannelId.B, rollback: _portA, cancellationToken);

        lock (_sync)
        {
            _splitterA = _newSplitter();
            _splitterB = _newSplitter();

            _previousFrameEndedAt = null;

        }

        StartedAt ??= _clock.Now;
        _flushCts = new CancellationTokenSource();
        _flushLoop = Task.Run(() => FlushLoopAsync(_flushCts.Token), CancellationToken.None);

        SetState(ConnectionState.Connected);
    }

    // Open a port with channel errors.
    private async Task OpenOrThrowAsync(
        ISerialPort port, ChannelId channel, ISerialPort? rollback, CancellationToken cancellationToken)
    {
        try
        {
            await port.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (rollback is not null)
            {
                try
                {
                    await rollback.CloseAsync(CancellationToken.None);
                }
                catch (Exception rollbackFailure)
                {
                    _logger.LogWarning(
                        rollbackFailure,
                        "Rolling back {PortName} after the other channel failed to open did not succeed; "
                        + "the port may stay busy in this process.",
                        rollback.PortName);
                }
            }

            SetState(ConnectionState.Faulted);
            throw new SerialPortOpenException(channel, port.PortName, ex);
        }
    }

    // Stop dual-port capture.
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_flushCts is { } cts)
        {
            await cts.CancelAsync();
            if (_flushLoop is not null)
            {
                try { await _flushLoop; }
                catch (OperationCanceledException) { }
            }

            cts.Dispose();
            _flushCts = null;
            _flushLoop = null;
        }

        await Task.WhenAll(Close(_portA), Close(_portB));

        EmitFrames(FlushRemaining());

        SetState(ConnectionState.Disconnected);

        // Close a monitored serial port.
        async Task Close(ISerialPort port) => await port.CloseAsync(cancellationToken);
    }

    // Send the requested payload.
    public async Task SendAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || data.IsEmpty) return;

        var port = channel == ChannelId.B ? _portB : _portA;

        var now = _clock.Now;
        EmitFrames([CreateFrame(new FramedData(data.ToArray(), now, now), channel, FrameDirection.Tx)]);

        await port.WriteAsync(data, cancellationToken);
    }

    // Release ports and subscriptions.
    public async ValueTask DisposeAsync()
    {
        _portA.DataReceived -= OnDataReceivedA;
        _portB.DataReceived -= OnDataReceivedB;
        _portA.ErrorReceived -= OnErrorReceivedA;
        _portB.ErrorReceived -= OnErrorReceivedB;

        await StopAsync();
        await _portA.DisposeAsync();
        await _portB.DisposeAsync();

        FrameCaptured = null;
        LineErrorDetected = null;
        StateChanged = null;
    }

    // Handle received channel data.

    private void OnDataReceivedA(object? sender, SerialChunkReceivedEventArgs e)
        => OnDataReceived(ChannelId.A, _splitterA, e);

    // Process channel B input bytes.
    private void OnDataReceivedB(object? sender, SerialChunkReceivedEventArgs e)
        => OnDataReceived(ChannelId.B, _splitterB, e);

    // Process incoming serial bytes.
    private void OnDataReceived(ChannelId channel, IFrameSplitter splitter, SerialChunkReceivedEventArgs e)
    {
        List<SerialFrame>? frames = null;

        lock (_sync)
        {
            foreach (var framed in splitter.Append(e.Data, e.Timestamp))
            {
                (frames ??= []).Add(CreateFrameLocked(framed, channel, FrameDirection.Rx));
            }
        }

        EmitFrames(frames);
    }

    // Process channel A line errors.
    private void OnErrorReceivedA(object? sender, SerialErrorEventArgs e) => OnErrorReceived(e);

    // Process channel B line errors.
    private void OnErrorReceivedB(object? sender, SerialErrorEventArgs e) => OnErrorReceived(e);

    // Process a serial line error.
    private void OnErrorReceived(SerialErrorEventArgs e)
    {
        if (!e.IsFatal)
        {
            LineErrorDetected?.Invoke(this, e);
            return;
        }

        EmitFrames(FlushRemaining());

        _flushCts?.Cancel();

        SetState(ConnectionState.Faulted, e.Message, e.Kind);
    }

    // Flush frames after idle gaps.
    private async Task FlushLoopAsync(CancellationToken token)
    {
        var gapMs = Math.Min(_splitterA.IdleGap.TotalMilliseconds, _splitterB.IdleGap.TotalMilliseconds);
        var period = TimeSpan.FromMilliseconds(Math.Clamp(
            gapMs, MinFlushPeriod.TotalMilliseconds, MaxFlushPeriod.TotalMilliseconds));

        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                List<SerialFrame>? frames = null;

                lock (_sync)
                {
                    var now = _clock.Now;

                    if (_splitterA.FlushIfIdle(now) is { } a)
                    {
                        (frames ??= []).Add(CreateFrameLocked(a, ChannelId.A, FrameDirection.Rx));
                    }

                    if (_splitterB.FlushIfIdle(now) is { } b)
                    {
                        (frames ??= []).Add(CreateFrameLocked(b, ChannelId.B, FrameDirection.Rx));
                    }
                }

                EmitFrames(frames);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Flush remaining buffered frames.
    private List<SerialFrame>? FlushRemaining()
    {
        List<SerialFrame>? frames = null;

        lock (_sync)
        {
            if (_splitterA.Flush() is { } a)
            {
                (frames ??= []).Add(CreateFrameLocked(a, ChannelId.A, FrameDirection.Rx));
            }

            if (_splitterB.Flush() is { } b)
            {
                (frames ??= []).Add(CreateFrameLocked(b, ChannelId.B, FrameDirection.Rx));
            }
        }

        return frames;
    }

    // Build a captured serial frame.
    private SerialFrame CreateFrame(FramedData framed, ChannelId channel, FrameDirection direction)
    {
        lock (_sync) return CreateFrameLocked(framed, channel, direction);
    }

    // Create a frame under the lock.
    private SerialFrame CreateFrameLocked(FramedData framed, ChannelId channel, FrameDirection direction)
    {
        var origin = StartedAt ?? framed.StartedAt;

        var delta = _previousFrameEndedAt is { } previous
            ? framed.StartedAt - previous
            : (TimeSpan?)null;

        _previousFrameEndedAt = framed.EndedAt;

        return new SerialFrame
        {
            Sequence = ++_sequence,
            Timestamp = framed.StartedAt,
            Elapsed = framed.StartedAt - origin,
            Delta = delta,
            Channel = channel,
            Direction = direction,
            Data = framed.Data
        };
    }

    // Publish completed serial frames.
    private void EmitFrames(List<SerialFrame>? frames)
    {
        if (frames is null) return;

        foreach (var frame in frames)
        {
            if (frame.Delta is { Ticks: < 0 } negative)
            {
                SessionLog.NegativeDelta(
                    _logger, frame.Sequence, frame.Channel, negative.TotalMilliseconds);
            }

            FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frame));
        }
    }

    // Publish the connection state.
    private void SetState(
        ConnectionState state,
        string? message = null,
        SerialErrorKind errorKind = SerialErrorKind.Unknown)
    {
        if (State == state) return;

        var from = State;
        State = state;
        SessionLog.StateTransition(
            _logger, from, state, Kind, $"{_portA.PortName}+{_portB.PortName}");

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message, errorKind));
    }
}
