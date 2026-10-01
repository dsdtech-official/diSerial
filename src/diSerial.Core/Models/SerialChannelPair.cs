namespace DiSerial.Core.Models;

// Hold the two channel settings.
public sealed record SerialChannelPair
{
    public required SerialPortInfo ChannelA { get; init; }

    public required SerialPortInfo ChannelB { get; init; }

    // Swap channel A and B settings.
    public SerialChannelPair Swap() => this with { ChannelA = ChannelB, ChannelB = ChannelA };

    // Describe both serial channels.
    public override string ToString() => $"{ChannelA.PortName} / {ChannelB.PortName}";
}
