namespace DiSerial.Core.Models;

// Hold serial port configuration.
public sealed record SerialPortSettings
{
    public static readonly IReadOnlyList<int> StandardBaudRates =
    [
        300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400,
        57600, 115200, 230400, 460800, 921600
    ];

    public int BaudRate { get; init; } = 9600;

    public int DataBits { get; init; } = 8;

    public SerialParity Parity { get; init; } = SerialParity.None;

    public SerialStopBits StopBits { get; init; } = SerialStopBits.One;

    public SerialFlowControl FlowControl { get; init; } = SerialFlowControl.None;

    public string ShortDescription
    {
        get
        {
            var parity = Parity switch
            {
                SerialParity.None => "N",
                SerialParity.Odd => "O",
                SerialParity.Even => "E",
                SerialParity.Mark => "M",
                SerialParity.Space => "S",
                _ => "?"
            };
            var stop = StopBits switch
            {
                SerialStopBits.One => "1",
                SerialStopBits.OnePointFive => "1.5",
                SerialStopBits.Two => "2",
                _ => "?"
            };
            return $"{BaudRate} {DataBits}{parity}{stop}";
        }
    }

    public TimeSpan DefaultIdleGap
    {
        get
        {
            var bitsPerChar = 1 + DataBits
                + (Parity == SerialParity.None ? 0 : 1)
                + StopBits switch
                {
                    SerialStopBits.OnePointFive => 1.5,
                    SerialStopBits.Two => 2,
                    _ => 1
                };
            var charMs = bitsPerChar * 1000.0 / BaudRate;
            return TimeSpan.FromMilliseconds(Math.Max(charMs * 3.5, 1.75));
        }
    }
}
