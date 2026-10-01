using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IPortEnumerator
{
    // List available serial ports.
    Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken cancellationToken = default);
}
