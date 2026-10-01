using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Replay;

// List replay and physical ports.
public sealed class ReplayAwarePortEnumerator(IPortEnumerator inner) : IPortEnumerator
{
    // List available serial ports.
    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(
        CancellationToken cancellationToken = default)
    {
        var real = await inner.GetPortsAsync(cancellationToken);

        var replay = ReplayScenarios.All.Select(s => new SerialPortInfo
        {
            PortName = ReplayScenarios.PortNameFor(s),
            Description = s.Description
        });

        return [.. real, .. replay];
    }
}

// Create replay or physical ports.
public sealed class ReplayAwareSerialPortFactory(
    ISerialPortFactory inner,
    IMonotonicClock clock,
    ReplayOptions options,
    ILoggerFactory? loggerFactory = null) : ISerialPortFactory
{
    public PlatformSupportStatus SupportStatus => inner.SupportStatus;

    // Create a live or replay port.
    public ISerialPort Create(string portName, SerialPortSettings settings)
    {
        if (ReplayScenarios.FindByPortName(portName) is { } script)
        {
            return new ReplaySerialPort(
                portName, settings, script, options, clock,
                loggerFactory?.CreateLogger($"Serial.{portName}"));
        }

        return inner.Create(portName, settings);
    }
}
