using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Replay;

/// <summary>
/// 内置回放场景。每个场景对应一个虚拟端口，出现在端口列表里可直接选用。
///
/// 场景的取值刻意贴近真实：Modbus RTU 的 4ms 级响应延迟、96ms 轮询间隔，
/// 都是现场抓包里的典型数量级。时序失真会让这个工具失去意义。
/// </summary>
public static class ReplayScenarios
{
    /// <summary>虚拟端口名的统一前缀，供工厂识别并拦截。</summary>
    public const string PortPrefix = "REPLAY";

    /// <summary>
    /// 场景一：Modbus RTU 总线轮询。
    ///
    /// 一问一答 + 长静默的节奏，正好覆盖分帧的两个关键点：
    ///   - 4.2ms 的请求→应答间隔（大于 9600 8N1 的 3.65ms 阈值）应切成两帧
    ///   - 末尾的长静默只能靠<b>空闲刷新定时器</b>把最后一帧吐出来
    /// 其中一次应答是异常码，用来看异常帧的着色。
    /// </summary>
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
            // ⚠️ 此处曾误设为 3.4ms。9600 8N1 的 3.5 字符时间是 3.646ms，
            // 低于该值的间隔<b>违反 Modbus RTU 的帧间静默规定</b>，真实设备不会这样应答；
            // 分帧器（正确地）不切分，结果两帧被并成一行 —— 看起来像 bug，实为脚本数据有误。
            // 回放工具自己把这个问题暴露了出来。
            new(TimeSpan.FromMilliseconds(5.1),
                [0x01, 0x06, 0x00, 0x10, 0x00, 0x01, 0x48, 0x0F], "回显确认"),

            new(TimeSpan.FromMilliseconds(96),
                [0x02, 0x03, 0x00, 0x00, 0x00, 0x0A, 0x25, 0xCD], "读从站 02"),
            new(TimeSpan.FromMilliseconds(4.9),
                [0x02, 0x83, 0x02, 0xC0, 0xF1], "异常码 02"),

            // 刻意留一段长静默：此时最后一帧仍在分帧器缓冲区里，
            // 只有 FlushIfIdle 定时器才能把它显示出来。
            new(TimeSpan.FromMilliseconds(1200),
                [0x01, 0x03, 0x00, 0x00, 0x00, 0x02, 0xC4, 0x0B], "静默后重新轮询")
        ]
    };

    /// <summary>场景二：AT 指令交互。慢速纯文本，用来看 ASCII 显示与行尾处理。</summary>
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

    /// <summary>
    /// 场景三：高速突发。每 2ms 一帧，用来压 UI 的批量刷新路径。
    ///
    /// 显示区当前仍是 ItemsControl 脚手架，这个场景可以直观看出
    /// 它在什么量级开始吃力，为「换自绘控件」这项决策提供依据。
    /// </summary>
    public static readonly ReplayScript HighRateBurst = new()
    {
        Id = "burst",
        Description = "Replay: high-rate burst (dev)",
        Steps = BuildBurst()
    };

    /// <summary>
    /// Scenario four: line faults, then a fatal one (P2-36, added 2026-08-05).
    ///
    /// <para>⭐ <b>What it is for</b>: until this existed, every fault path could only be
    /// reached by physically unplugging a cable — see <see cref="ReplayFault"/> for why that
    /// made them this project's most expensive thing to verify. This walks the whole
    /// sequence: healthy traffic → a non-fatal parity error (P1-52: the session must show a
    /// notice and <b>keep running</b>) → traffic again, proving it really did keep running →
    /// a fatal removal (P1-54: the session must go Faulted and must not claim it is still
    /// connected).</para>
    ///
    /// <para>⚠️ <b><c>Loop = false</c>, and that is not a detail</b>: the script ends on a
    /// fatal fault, and a port that came back to life on the next iteration would model
    /// hardware that does not exist. The replay loop stops there too.</para>
    ///
    /// <para>⛔ <b>Read a green run correctly</b>: it says the behaviour <i>after</i> a fault
    /// is right. It does not say real hardware raises these, and it does not cover the port
    /// disappearing from enumeration. The full boundary is on <see cref="ReplayFault"/>.</para>
    /// </summary>
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

            // Non-fatal: a notice, not a failure. The session must stay connected -- the
            // frames after this one are what prove it did.
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

            // Fatal: everything after this point in the script is unreachable by design.
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

    /// <summary>虚拟端口名 → 场景。端口名形如 REPLAY-MODBUS。</summary>
    public static string PortNameFor(ReplayScript script) =>
        $"{PortPrefix}-{script.Id.ToUpperInvariant()}";

    public static ReplayScript? FindByPortName(string portName) =>
        All.FirstOrDefault(s => string.Equals(
            PortNameFor(s), portName, StringComparison.OrdinalIgnoreCase));

    public static bool IsReplayPort(string portName) =>
        portName.StartsWith(PortPrefix + "-", StringComparison.OrdinalIgnoreCase);

    private static ReplayStep[] BuildBurst()
    {
        var steps = new ReplayStep[120];
        for (var i = 0; i < steps.Length; i++)
        {
            // 帧内容带序号，便于肉眼确认没有丢帧或乱序。
            var seq = (ushort)(i + 1);
            steps[i] = new ReplayStep(
                TimeSpan.FromMilliseconds(i == 0 ? 500 : 2),
                [0x11, (byte)(seq >> 8), (byte)(seq & 0xFF), 0x22, 0x33, 0x44]);
        }
        return steps;
    }
}
