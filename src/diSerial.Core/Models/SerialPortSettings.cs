namespace DiSerial.Core.Models;

/// <summary>
/// 串口参数（C-04a）。
/// V1.0 仅支持标准波特率列表；自定义波特率需平台特定实现（Linux TCSETS2 /
/// macOS IOSSIOSPEED），排期在 V1.1。
/// </summary>
public sealed record SerialPortSettings
{
    /// <summary>V1.0 提供的标准波特率列表。</summary>
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

    /// <summary>
    /// The conventional short notation for these settings, e.g. <c>9600 8N1</c>.
    ///
    /// <para>⚠️ <b>Not a presentation concern, despite how it reads</b> (P2-34, corrected
    /// 2026-08-05). The comment here used to say "for the status bar and title", which made
    /// this look like UI formatting that had leaked into the domain layer — and it was listed
    /// as exactly that. The actual usage says otherwise: of its four call sites, <b>three
    /// build file names</b> (both session view models' default export name, and
    /// <c>SqliteRecordingReader</c>'s batch <c>SettingsLabel</c>), and one is the settings
    /// panel's display string.</para>
    ///
    /// <para>⭐ So what this really is: <b>the single canonical encoding of a port
    /// configuration</b>, in the notation the entire serial world already uses. It belongs
    /// with the settings precisely because it must be identical everywhere — a second
    /// abbreviation table written next to a file-name builder is how <c>9600 8N1</c> and
    /// <c>9600-8-N-1</c> end up in the same product. <c>SqliteRecordingReader</c> says this
    /// out loud at its own call site.</para>
    ///
    /// <para>⚠️ Callers that put it in a file name replace the space with a hyphen themselves;
    /// this property does not, because the space is correct in the notation.</para>
    /// </summary>
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

    /// <summary>
    /// The Modbus RTU 3.5-character idle interval, used as the default threshold for idle
    /// framing (C-07). One character = start bit + data bits + parity bit + stop bits.
    ///
    /// <para>⛔ <b>1.5 stop bits used to count as 1</b> (P2-34, fixed 2026-08-05). The
    /// expression was <c>StopBits == Two ? 2 : 1</c>, so a <c>OnePointFive</c> configuration
    /// produced a character half a bit too short and therefore a threshold that was <b>not
    /// the 3.5 character times 01-spec C-07 promises</b> — about 5% short at 8N1.5. The
    /// mismatch was visible in this very file: <see cref="ShortDescription"/> renders the
    /// same setting as <c>1.5</c>.</para>
    ///
    /// <para>⚠️ Consequence of the old value, for anyone reading old captures: a threshold
    /// that is too short splits frames <b>more eagerly</b> than it should, so a 1.5-stop-bit
    /// session could show one protocol frame as two rows. It never merged frames that should
    /// have been separate — the error only ever went in the safe direction.</para>
    /// </summary>
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
            // At high baud rates 3.5 character times is vanishingly small; the Modbus
            // specification clamps the threshold at a 1.75ms floor.
            return TimeSpan.FromMilliseconds(Math.Max(charMs * 3.5, 1.75));
        }
    }
}
