using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

// Poll serial port availability.
public sealed class PollingDeviceWatcher(
    IPortEnumerator portEnumerator,
    ILogger? logger = null,
    TimeSpan? pollInterval = null)
    : IDeviceWatcher
{
    private readonly ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;

    private readonly Lock _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IReadOnlyList<SerialPortInfo> _lastSnapshot = [];

    private bool _starting;

    private volatile bool _isRunning;

    public bool IsRunning { get => _isRunning; private set => _isRunning = value; }

    public event EventHandler<PortsChangedEventArgs>? PortsChanged;

    // Start watching serial ports.
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isRunning || _starting) return;
            _starting = true;
        }

        try
        {
            var baselineKnown = true;
            try
            {
                _lastSnapshot = await portEnumerator.GetPortsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                baselineKnown = false;
                _lastSnapshot = [];
                DeviceWatcherLog.BaselineFailed(_logger, e, e.GetType().Name);
            }

            if (baselineKnown)
            {
                DeviceWatcherLog.Started(
                    _logger, _lastSnapshot.Count, DeviceWatcherLog.Describe(_lastSnapshot));
            }

            var cts = new CancellationTokenSource();
            var loop = Task.Run(() => PollLoopAsync(cts.Token, baselineKnown), CancellationToken.None);

            lock (_sync)
            {
                _cts = cts;
                _loop = loop;
            }

            IsRunning = true;
        }
        finally
        {
            lock (_sync) _starting = false;
        }
    }

    // Stop watching serial ports.
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not { } cts) return;

        await cts.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        cts.Dispose();
        _cts = null;
        _loop = null;
        IsRunning = false;

        DeviceWatcherLog.Stopped(_logger);
    }

    // Release owned resources.

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        PortsChanged = null;
    }

    // Watch for serial port changes.
    private async Task PollLoopAsync(CancellationToken token, bool baselineKnown = true)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        var needsBaseline = !baselineKnown;

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                IReadOnlyList<SerialPortInfo> current;
                try
                {
                    current = await portEnumerator.GetPortsAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogDebug(e, "Port enumeration failed for this poll; skipping the round.");
                    continue;
                }

                if (needsBaseline)
                {
                    needsBaseline = false;
                    lock (_sync) _lastSnapshot = current;

                    DeviceWatcherLog.BaselineRecovered(
                        _logger, current.Count, DeviceWatcherLog.Describe(current));
                    continue;
                }

                Compare(current, out var added, out var removed);
                if (added.Count == 0 && removed.Count == 0)
                {
                    DeviceWatcherLog.PolledNoChange(_logger, current.Count);
                    continue;
                }

                DeviceWatcherLog.PortsChanged(
                    _logger, added.Count, removed.Count, current.Count,
                    DeviceWatcherLog.Describe(added),
                    DeviceWatcherLog.Describe(removed),
                    DeviceWatcherLog.Describe(current));

                try
                {
                    PortsChanged?.Invoke(this, new PortsChangedEventArgs(added, removed, current));
                }
                catch (Exception e)
                {
                    DeviceWatcherLog.SubscriberFailed(_logger, e, e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            DeviceWatcherLog.Faulted(_logger, e, e.GetType().Name);
            IsRunning = false;
        }
    }

    // Compare serial port snapshots.
    private void Compare(
        IReadOnlyList<SerialPortInfo> current,
        out IReadOnlyList<SerialPortInfo> added,
        out IReadOnlyList<SerialPortInfo> removed)
    {
        lock (_sync)
        {
            var previousNames = _lastSnapshot
                .Select(p => p.PortName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var currentNames = current
                .Select(p => p.PortName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            added = current.Where(p => !previousNames.Contains(p.PortName)).ToArray();
            removed = _lastSnapshot.Where(p => !currentNames.Contains(p.PortName)).ToArray();

            _lastSnapshot = current;
        }
    }
}
