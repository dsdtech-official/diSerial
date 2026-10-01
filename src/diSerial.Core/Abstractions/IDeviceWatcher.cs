using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IDeviceWatcher : IAsyncDisposable
{
    bool IsRunning { get; }

    event EventHandler<PortsChangedEventArgs>? PortsChanged;

    // Start watching serial ports.
    Task StartAsync(CancellationToken cancellationToken = default);

    // Stop watching serial ports.
    Task StopAsync(CancellationToken cancellationToken = default);
}

// Carry the current port list.
public sealed class PortsChangedEventArgs(
    IReadOnlyList<SerialPortInfo> added,
    IReadOnlyList<SerialPortInfo> removed,
    IReadOnlyList<SerialPortInfo> current) : EventArgs
{
    public IReadOnlyList<SerialPortInfo> Added { get; } = added;

    public IReadOnlyList<SerialPortInfo> Removed { get; } = removed;

    public IReadOnlyList<SerialPortInfo> Current { get; } = current;
}
