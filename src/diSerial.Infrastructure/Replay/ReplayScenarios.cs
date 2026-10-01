using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

// Define synthetic replay data.
public static class ReplayScenarios
{
    public const string PortPrefix = "REPLAY";

    public static readonly ReplayScript ModbusPolling = new()
    {
        Id = "modbus",
        Description = "Replay: Modbus RTU polling (dev)",
        Steps =
        [
            new(TimeSpan.FromMilliseconds(300),
                [0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0xCD], "读从站 01 保持寄存器"),
            new(TimeSpan.FromMilliseconds(4.2),
                [0x01, 0x03, 0x14, 0x00, 0x64, 0x00, 0xC8, 0x00, 0x2C, 0x01, 0x90,
                 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x3C], "应答"),

            new(TimeSpan.FromMilliseconds(96),
                [0x01, 0x06, 0x00, 0x10, 0x00, 0x01, 0x48, 0x0F], "写单寄存器"),
            new(TimeSpan.FromMilliseconds(5.1),
                [0x01, 0x06, 0x00, 0x10, 0x00, 0x01, 0x48, 0x0F], "回显确认"),

            new(TimeSpan.FromMilliseconds(96),
                [0x02, 0x03, 0x00, 0x00, 0x00, 0x0A, 0x25, 0xCD], "读从站 02"),
            new(TimeSpan.FromMilliseconds(4.9),
                [0x02, 0x83, 0x02, 0xC0, 0xF1], "异常码 02"),

            new(TimeSpan.FromMilliseconds(1200),
                [0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B], "静默后重新轮询")
        ]
    };

    public static readonly ReplayScript AtCommands = new()
    {
        Id = "at",
        Description = "Replay: AT command exchange (dev)",
        Steps =
        [
            new(TimeSpan.FromMilliseconds(800), "AT\r\n"u8.ToArray()),
            new(TimeSpan.FromMilliseconds(120), "OK\r\n"u8.ToArray()),
            new(TimeSpan.FromMilliseconds(900), "AT+GMR\r\n"u8.ToArray()),
            new(TimeSpan.FromMilliseconds(150), "v1.2.3\r\nOK\r\n"u8.ToArray()),
            new(TimeSpan.FromMilliseconds(1500), "+READY\r\n"u8.ToArray())
        ]
    };

    public static readonly ReplayScript HighRateBurst = new()
    {
        Id = "burst",
        Description = "Replay: high-rate burst (dev)",
        Steps = BuildBurst()
    };

    public static readonly ReplayScript LineFaults = new()
    {
        Id = "faults",
        Description = "Replay: line errors then device removal (dev)",
        Loop = false,
        Steps =
        [
            new(TimeSpan.FromMilliseconds(400),
                [0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0xCD], "healthy request"),
            new(TimeSpan.FromMilliseconds(4.2),
                [0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8, 0xBA, 0x2F], "healthy response"),

            new(TimeSpan.FromMilliseconds(300), [], "parity error on the line")
            {
                Fault = new ReplayFault(
                    SerialErrorKind.Unknown,
                    "Replay: scripted parity error",
                    IsFatal: false,
                    FrameFlags.ParityError)
            },

            new(TimeSpan.FromMilliseconds(120),
                [0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B], "still alive after the error"),
            new(TimeSpan.FromMilliseconds(4.6),
                [0x01, 0x03, 0x04, 0x00, 0x01, 0x00, 0x02, 0x2B, 0x0C], "response"),

            new(TimeSpan.FromMilliseconds(600), [], "device removed")
            {
                Fault = new ReplayFault(
                    SerialErrorKind.DeviceRemoved,
                    "Replay: scripted device removal",
                    IsFatal: true)
            }
        ]
    };

    public static IReadOnlyList<ReplayScript> All { get; } =
        [ModbusPolling, AtCommands, HighRateBurst, LineFaults];

    // Build a replay port name.
    public static string PortNameFor(ReplayScript script) =>
        $"{PortPrefix}-{script.Id.ToUpperInvariant()}";

    // Find a replay scenario by port.
    public static ReplayScript? FindByPortName(string portName) =>
        All.FirstOrDefault(s => string.Equals(
            PortNameFor(s), portName, StringComparison.OrdinalIgnoreCase));

    // Identify a replay port.
    public static bool IsReplayPort(string portName) =>
        portName.StartsWith(PortPrefix + "-", StringComparison.OrdinalIgnoreCase);

    // Build the burst replay scenario.
    private static ReplayStep[] BuildBurst()
    {
        var steps = new ReplayStep[120];
        for (var i = 0; i < steps.Length; i++)
        {
            var seq = (ushort)(i + 1);
            steps[i] = new ReplayStep(
                TimeSpan.FromMilliseconds(i == 0 ? 500 : 2),
                [0x11, (byte)(seq >> 8), (byte)(seq & 0xFF), 0x22, 0x33, 0x44]);
        }
        return steps;
    }
}
