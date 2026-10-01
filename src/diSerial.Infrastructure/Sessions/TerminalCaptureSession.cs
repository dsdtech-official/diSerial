using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.Infrastructure.Sessions;

// Capture a terminal port.
public sealed class TerminalCaptureSession
    : ICaptureSession, IControlLineSession, IAutoAnswerSession
{
    private static readonly TimeSpan MinFlushPeriod = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan MaxFlushPeriod = TimeSpan.FromMilliseconds(50);

    private readonly ISerialPort _port;
    private readonly Func<IFrameSplitter> _newSplitter;

    private IFrameSplitter _splitter;
    private readonly IMonotonicClock _clock;
    private readonly ILogger _logger;
    private readonly Lock _sync = new();

    private CancellationTokenSource? _flushCts;
    private Task? _flushLoop;
    private long _sequence;
    private DateTimeOffset? _previousFrameEndedAt;

    // Initialize terminal capture.
    public TerminalCaptureSession(
        ISerialPort port,
        Func<IFrameSplitter> newSplitter,
        IMonotonicClock clock,
        ILogger? logger = null)
    {
        _port = port;
        _newSplitter = newSplitter;

        _splitter = newSplitter();
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;

        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
    }

    public SessionKind Kind => SessionKind.Terminal;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public DateTimeOffset? StartedAt { get; private set; }

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SerialErrorEventArgs>? LineErrorDetected;

    // Start terminal capture.
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected) return;

        SetState(ConnectionState.Connecting);
        try
        {
            await _port.OpenAsync(cancellationToken);
        }
        catch
        {
            SetState(ConnectionState.Faulted);
            throw;
        }

        lock (_sync)
        {
            _splitter = _newSplitter();

            _previousFrameEndedAt = null;

        }

        StartedAt ??= _clock.Now;
        _flushCts = new CancellationTokenSource();
        _flushLoop = Task.Run(() => FlushLoopAsync(_flushCts.Token), CancellationToken.None);

        SetState(ConnectionState.Connected);
    }

    // Stop terminal capture.
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

        await _port.CloseAsync(cancellationToken);

        EmitFrames(FlushRemaining());

        SetState(ConnectionState.Disconnected);
    }

    // Send the requested payload.
    public Task SendAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => WriteAsync(data, isAutoAnswer: false, cancellationToken);

    // Send an automatic reply payload.
    public Task SendAutoAnswerAsync(
        ChannelId channel, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => WriteAsync(data, isAutoAnswer: true, cancellationToken);

    // Write a session payload.
    private async Task WriteAsync(
        ReadOnlyMemory<byte> data, bool isAutoAnswer, CancellationToken cancellationToken)
    {
        if (State != ConnectionState.Connected || data.IsEmpty) return;

        var now = _clock.Now;
        EmitFrames([CreateFrame(
            new FramedData(data.ToArray(), now, now), FrameDirection.Tx, isAutoAnswer)]);

        await _port.WriteAsync(data, cancellationToken);
    }

    // Read serial input line states.
    public SerialControlLines ReadControlLines() => _port.ReadControlLines();

    public bool IsRtsOwnedByFlowControl =>
        _port.Settings.FlowControl == SerialFlowControl.RequestToSend;

    // Set a serial output line.
    public Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default)
        => _port.SetOutputLineAsync(line, asserted, cancellationToken);

    // Release port and subscriptions.
    public async ValueTask DisposeAsync()
    {
        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;

        await StopAsync();
        await _port.DisposeAsync();

        FrameCaptured = null;
        LineErrorDetected = null;
        StateChanged = null;
    }

    // Process incoming serial bytes.
    private void OnDataReceived(object? sender, SerialChunkReceivedEventArgs e)
    {
        List<SerialFrame>? frames = null;

        lock (_sync)
        {
            foreach (var framed in _splitter.Append(e.Data, e.Timestamp))
            {
                (frames ??= []).Add(CreateFrameLocked(framed, FrameDirection.Rx));
            }
        }

        EmitFrames(frames);
    }

    // Process a serial line error.
    private void OnErrorReceived(object? sender, SerialErrorEventArgs e)
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
        var period = TimeSpan.FromMilliseconds(Math.Clamp(
            _splitter.IdleGap.TotalMilliseconds,
            MinFlushPeriod.TotalMilliseconds,
            MaxFlushPeriod.TotalMilliseconds));

        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                List<SerialFrame>? frames = null;
                lock (_sync)
                {
                    if (_splitter.FlushIfIdle(_clock.Now) is { } framed)
                    {
                        frames = [CreateFrameLocked(framed, FrameDirection.Rx)];
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
        lock (_sync)
        {
            return _splitter.Flush() is { } framed
                ? [CreateFrameLocked(framed, FrameDirection.Rx)]
                : null;
        }
    }

    // Build a captured serial frame.
    private SerialFrame CreateFrame(
        FramedData framed, FrameDirection direction, bool isAutoAnswer = false)
    {
        lock (_sync) return CreateFrameLocked(framed, direction, isAutoAnswer);
    }

    // Create a frame under the lock.
    private SerialFrame CreateFrameLocked(
        FramedData framed, FrameDirection direction, bool isAutoAnswer = false)
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
            Channel = ChannelId.None,
            Direction = direction,
            IsAutoAnswer = isAutoAnswer,
            Data = framed.Data
        };
    }

    // Publish completed serial frames.
    private void EmitFrames(List<SerialFrame>? frames)
    {
        if (frames is null) return;

        foreach (var frame in frames)
        {
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
        SessionLog.StateTransition(_logger, from, state, Kind, _port.PortName);

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message, errorKind));
    }
}
