using System.Diagnostics;
using System.IO.Ports;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CoreParity = DiSerial.Core.Models.SerialParity;
using CoreStopBits = DiSerial.Core.Models.SerialStopBits;
using CoreFlowControl = DiSerial.Core.Models.SerialFlowControl;

namespace DiSerial.Infrastructure.Serial;

// Adapt runtime serial ports.
public sealed class SystemIoSerialPort : ISerialPort
{
    private const int ReadBufferSize = 4096;

    private readonly IMonotonicClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;
    private readonly bool _includePayload;

    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    private volatile bool _dtrAsserted;

    private volatile bool _rtsAsserted;

    private int _disposed;

    // Initialize the serial adapter.
    public SystemIoSerialPort(
        string portName,
        SerialPortSettings settings,
        IMonotonicClock clock,
        ILogger? logger = null,
        bool includePayload = false)
    {
        PortName = portName;
        Settings = settings;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
        _includePayload = includePayload;
    }

    public string PortName { get; }

    public SerialPortSettings Settings { get; }

    public bool IsOpen => _port?.IsOpen == true;

    public event EventHandler<SerialChunkReceivedEventArgs>? DataReceived;

    public event EventHandler<SerialErrorEventArgs>? ErrorReceived;

    // Open the serial port.
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsOpen) return;

            LogOpening();
            var started = Stopwatch.GetTimestamp();

            var port = new SerialPort(PortName)
            {
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 2000,
                ReadBufferSize = 1 << 16
            };

            try
            {
                Apply(port, Settings);
                ApplyOutputLines(port);
                port.Open();
            }
            catch (Exception ex)
            {
                SerialPortLog.OpenFailed(
                    _logger, ex, PortName,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
                port.Dispose();

                if (ex is UnauthorizedAccessException
                    && ProbeDeviceNodeOnOpenFailure
                    && IsAbsentDeviceNode(PortName, File.Exists))
                {
                    SerialPortLog.AbsentDeviceNodeOnOpen(_logger, PortName, ex.GetType().Name);

                    throw new FileNotFoundException(
                        $"The serial port '{PortName}' does not exist.", PortName, ex);
                }

                throw;
            }

            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            _lineErrorsSeen = FrameFlags.None;
            port.ErrorReceived += OnPortErrorReceived;

            await CollectStaleReadLoopAsync();

            _port = port;
            _readCts = new CancellationTokenSource();
            _readLoop = Task.Run(() => ReadLoopAsync(port, _readCts.Token), CancellationToken.None);

            SerialPortLog.Opened(_logger, PortName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ProbeDeviceNodeOnOpenFailure => !OperatingSystem.IsWindows();

    // Identify missing serial devices.
    public static bool IsAbsentDeviceNode(string portName, Func<string, bool> nodeExists)
    {
        ArgumentNullException.ThrowIfNull(nodeExists);

        if (string.IsNullOrEmpty(portName)) return false;
        if (portName[0] != '/') return false;

        return !nodeExists(portName);
    }

    // Close the serial port.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_port is null && _readCts is null) return;

            SerialPortLog.Closing(_logger, PortName);
            var started = Stopwatch.GetTimestamp();

            _readCts?.Cancel();

            var port = Interlocked.Exchange(ref _port, null);

            if (port is not null)
            {
                port.ErrorReceived -= OnPortErrorReceived;

                try
                {
                    if (port.IsOpen) port.Close();
                }
                catch (IOException ex)
                {
                    SerialPortLog.CloseFailed(_logger, ex, PortName, ex.GetType().Name);
                }
            }

            var timedOut = await AwaitReadLoopAsync();

            port?.Dispose();

            SerialPortLog.Closed(
                _logger, PortName, Stopwatch.GetElapsedTime(started).TotalMilliseconds, timedOut);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Release a disconnected port.
    private void DisposeDeadPort(SerialPort dead)
    {
        Interlocked.CompareExchange(ref _port, null, dead);

        dead.ErrorReceived -= OnPortErrorReceived;

        try
        {
            dead.Dispose();
        }
        catch (Exception ex)
        {
            SerialPortLog.CloseFailed(_logger, ex, PortName, ex.GetType().Name);
        }
    }

    private FrameFlags _lineErrorsSeen = FrameFlags.None;

    // Handle a serial port error.
    private void OnPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        var flags = MapLineError(e.EventType);
        if (flags == FrameFlags.None) return;

        var firstOfItsKind = (_lineErrorsSeen & flags) == FrameFlags.None;
        _lineErrorsSeen |= flags;

        SerialPortLog.LineError(_logger, PortName, e.EventType.ToString(), firstOfItsKind);

        if (!firstOfItsKind) return;

        ErrorReceived?.Invoke(this, new SerialErrorEventArgs(
            flags,
            $"Serial line error on {PortName}: {e.EventType}",
            isFatal: false,
            kind: SerialErrorKind.LineError));
    }

    // Map a native serial line error.
    private static FrameFlags MapLineError(SerialError error) => error switch
    {
        SerialError.RXParity => FrameFlags.ParityError,
        SerialError.Frame => FrameFlags.FramingError,

        SerialError.Overrun => FrameFlags.BufferOverrun,
        SerialError.RXOver => FrameFlags.BufferOverrun,

        _ => FrameFlags.None
    };

    // Write the supplied payload.
    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var port = _port;
            if (port is null || !port.IsOpen)
            {
                throw new InvalidOperationException($"Port {PortName} is not open.");
            }

            try
            {
                await port.BaseStream.WriteAsync(data, cancellationToken);
                await port.BaseStream.FlushAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SerialPortLog.WriteFailed(_logger, ex, PortName, data.Length, ex.GetType().Name);
                throw;
            }

            SerialPortLog.Wrote(_logger, PortName, data.Length);

            if (_includePayload && _logger.IsEnabled(LogLevel.Trace))
            {
                SerialPortLog.ReadPayload(_logger, PortName, SerialPortLog.ToHex(data.Span));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Release owned resources.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await CloseAsync();

        DataReceived = null;
        ErrorReceived = null;

    }

    // Log a stopped read loop.
    private void ReportReadLoopStop(
        Exception ex, SerialPort port, CancellationToken token, long reads, long bytes)
    {
        var kind = SerialErrorClassifier.ClassifyReadLoopStop(ex, token.IsCancellationRequested);

        if (kind is null)
        {
            SerialPortLog.ReadLoopExited(_logger, PortName, reads, bytes);
            return;
        }

        SerialPortLog.ReadLoopFaulted(_logger, ex, PortName, reads, bytes, ex.GetType().Name);

        DisposeDeadPort(port);

        ErrorReceived?.Invoke(this,
            new SerialErrorEventArgs(
                FrameFlags.None, ex.Message, isFatal: true, kind: kind.Value));
    }

    // Read incoming serial data.
    private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
    {
        var buffer = new byte[ReadBufferSize];

        long reads = 0, bytes = 0;
        DateTimeOffset? previous = null;

        Stream stream;
        try
        {
            stream = port.BaseStream;
        }
        catch (Exception ex)
        {
            ReportReadLoopStop(ex, port, token, reads, bytes);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            int count;
            try
            {
                count = await stream.ReadAsync(buffer, token);
            }
            catch (Exception ex)
            {
                ReportReadLoopStop(ex, port, token, reads, bytes);
                return;
            }

            var timestamp = _clock.Now;

            if (count <= 0) continue;

            reads++;
            bytes += count;

            var gapMs = previous is { } p ? (timestamp - p).TotalMilliseconds : 0d;
            previous = timestamp;
            SerialPortLog.ReadChunk(_logger, PortName, count, gapMs, timestamp);

            if (_includePayload && _logger.IsEnabled(LogLevel.Trace))
            {
                SerialPortLog.ReadPayload(
                    _logger, PortName, SerialPortLog.ToHex(buffer.AsSpan(0, count)));
            }

            var payload = new byte[count];
            buffer.AsSpan(0, count).CopyTo(payload);
            DataReceived?.Invoke(this, new SerialChunkReceivedEventArgs(payload, timestamp));
        }

        SerialPortLog.ReadLoopExited(_logger, PortName, reads, bytes);
    }

    // Wait for the serial read loop.
    private async Task<bool> AwaitReadLoopAsync()
    {
        if (_readCts is null) return false;

        var timedOut = false;

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                SerialPortLog.ReadLoopStopTimedOut(_logger, e, PortName);
                timedOut = true;
            }
            catch (Exception e)
            {
                SerialPortLog.ReadLoopStopFaulted(_logger, e, PortName, e.GetType().Name);
            }
        }

        _readCts.Dispose();
        _readCts = null;
        _readLoop = null;

        return timedOut;
    }

    private static readonly TimeSpan ReadLoopUnwindTimeout = TimeSpan.FromMilliseconds(500);

    // Collect an earlier read loop.
    private async Task CollectStaleReadLoopAsync()
    {
        if (Interlocked.Exchange(ref _readCts, null) is not { } stale) return;

        var staleLoop = _readLoop;
        _readLoop = null;

        if (staleLoop is not null)
        {
            try
            {
                await staleLoop.WaitAsync(ReadLoopUnwindTimeout);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                SerialPortLog.ReadLoopStopTimedOut(_logger, e, PortName);
            }
        }

        stale.Dispose();
    }

    // Log serial port configuration.
    private void LogOpening() => SerialPortLog.Opening(
        _logger, PortName, Settings.BaudRate, Settings.DataBits,
        Settings.Parity.ToString(), Settings.StopBits.ToString(), Settings.FlowControl.ToString());

    // Apply native serial settings.
    private static void Apply(SerialPort port, SerialPortSettings settings)
    {
        port.BaudRate = settings.BaudRate;
        port.DataBits = settings.DataBits;
        port.Parity = ToParity(settings.Parity);
        port.StopBits = ToStopBits(settings.StopBits);
        port.Handshake = ToHandshake(settings.FlowControl);

    }

    // Apply DTR and RTS output states.
    private void ApplyOutputLines(SerialPort port)
    {
        port.DtrEnable = _dtrAsserted;

        if (port.Handshake is not Handshake.RequestToSend)
        {
            port.RtsEnable = _rtsAsserted;
        }
    }

    // Read serial input line states.
    public SerialControlLines ReadControlLines()
    {
        var port = _port;
        if (port is null || !port.IsOpen) return SerialControlLines.Unknown;

        try
        {
            var lines = new SerialControlLines(
                ToState(port.CtsHolding), ToState(port.DsrHolding), ToState(port.CDHolding));

            _controlLineReadFailing = false;
            return lines;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or ObjectDisposedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {

            if (!_controlLineReadFailing)
            {
                _controlLineReadFailing = true;
                SerialPortLog.ControlLineReadFailed(_logger, ex, PortName, ex.GetType().Name);
            }

            return SerialControlLines.Unknown;
        }
    }

    private volatile bool _controlLineReadFailing;

    // Map a native control line state.
    private static ControlLineState ToState(bool asserted) =>
        asserted ? ControlLineState.High : ControlLineState.Low;

    // Set a serial output line.
    public async Task SetOutputLineAsync(
        SerialOutputLine line, bool asserted, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            switch (line)
            {
                case SerialOutputLine.Dtr: _dtrAsserted = asserted; break;
                case SerialOutputLine.Rts: _rtsAsserted = asserted; break;
                default: throw new ArgumentOutOfRangeException(nameof(line), line, null);
            }

            var port = _port;
            if (port is null || !port.IsOpen) return;

            try
            {
                ApplyOutputLines(port);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                           or ObjectDisposedException
                                           or IOException
                                           or UnauthorizedAccessException)
            {
                SerialPortLog.ControlLineFailed(_logger, ex, PortName, line.ToString(), ex.GetType().Name);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Map a parity setting.
    private static Parity ToParity(CoreParity value) => value switch
    {
        CoreParity.Odd => Parity.Odd,
        CoreParity.Even => Parity.Even,
        CoreParity.Mark => Parity.Mark,
        CoreParity.Space => Parity.Space,
        _ => Parity.None
    };

    // Map a stop bit setting.
    private static StopBits ToStopBits(CoreStopBits value) => value switch
    {
        CoreStopBits.OnePointFive => StopBits.OnePointFive,
        CoreStopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    // Map a flow control setting.
    private static Handshake ToHandshake(CoreFlowControl value) => value switch
    {
        CoreFlowControl.RequestToSend => Handshake.RequestToSend,
        CoreFlowControl.XOnXOff => Handshake.XOnXOff,
        _ => Handshake.None
    };
}
