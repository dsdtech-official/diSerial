using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Sessions;

// Create capture sessions.
public sealed class CaptureSessionFactory(
    ISerialPortFactory portFactory,
    IFrameSplitterFactory splitterFactory,
    IMonotonicClock clock,
    ILoggerFactory loggerFactory) : ICaptureSessionFactory
{
    // Create a terminal session.
    public ICaptureSession CreateTerminal(string portName, SerialPortSettings settings)
    {
        var port = portFactory.Create(portName, settings);

        return new TerminalCaptureSession(
            port, () => splitterFactory.Create(settings), clock, loggerFactory.CreateLogger("Session"));
    }

    // Create a monitoring session.
    public ICaptureSession CreateMonitor(SerialChannelPair pair, SerialPortSettings settings)
        => new MonitorCaptureSession(
            portFactory.Create(pair.ChannelA.PortName, settings),
            portFactory.Create(pair.ChannelB.PortName, settings),
            () => splitterFactory.Create(settings),
            clock,
            loggerFactory.CreateLogger("Session"));
}
