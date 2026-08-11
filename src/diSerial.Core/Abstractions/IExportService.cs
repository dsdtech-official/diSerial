using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 导出（M-08 与 C-09 的导出那一步）。规格见 docs/01-spec.md 6.4。
///
/// <b>两个来源共用本接口</b>：
///   - 「导出」按钮 —— 导的是**显示缓冲**（你看到的这一屏）
///   - 「停止记录」之后 —— 导的是**刚结束的那个批次**（从库里读回来的全量）
/// 两者的差别只在帧从哪来，渲染规则完全一致，所以刻意只有这一个接口。
/// </summary>
public interface IExportService
{
    IReadOnlyList<ExportFormat> SupportedFormats { get; }

    Task ExportAsync(
        IEnumerable<SerialFrame> frames,
        string filePath,
        ExportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 导出的文件类型。⚠️ <b>2026-08-11 用户定：去掉 <c>Txt</c>，加上 <c>Csv</c>。</b>
///
/// <para>⭐ <b>两者内容<u>不再相同</u></b> —— 分隔符不同，且 <c>Csv</c> 按 RFC 4180 加引号转义。
/// ⛔ <b>所以本枚举现在<u>真的被读</u>了</b>（<see cref="TabularExportService"/> 按它选分隔符与转义），
/// 而它此前是「调用方设置、无人读取」（P2-34）。<b>那条注释里写着「若某个格式开始渲染得不一样，
/// 那就是本字段获得意义、并且要复查每一个 producer 的时刻」—— 这就是那一刻。</b></para>
///
/// <para>⚠️ <b>本处此前写着「刻意没有 Csv」，那句话<u>没有被推翻，是条件不再成立了</u></b>：
/// 它反对的是「<b>名为 <c>.csv</c> 而内容是制表符分隔</b>」的文件 ——
/// Excel 双击会把整行塞进一列。⭐ <b>真正的逗号分隔 csv 不触发那条，而 <c>.txt</c> 走掉之后，
/// 「双击就能在 Windows 上打开」这个需求需要一个真的做得到的格式。</b></para>
///
/// <para>⛔⭐ <b>已知局限，明确接受（2026-08-11 用户定「纯逗号 RFC 4180」）</b>：
/// <b>Excel 的列表分隔符跟系统区域走</b> —— 德 / 法 / 西 / 意 / 葡 的 Windows 上默认是分号，
/// 那里<b>双击一个逗号分隔的 <c>.csv</c>，整行仍会塞进一格</b>。
/// ⚠️ <b>这不是缺陷报告，是<u>选路时算过的代价</u></b>：对策是 Excel 的
/// 「数据 → 从文本导入」，或首行加 <c>sep=,</c>（后者被否，因为它对严格解析器是一行数据，
/// 与 01-spec 6.4「不写注释行」直接冲突）。<b>别把它当成新发现再报一次。</b></para>
/// </summary>
public enum ExportFormat
{
    Tsv,
    Csv
}

public sealed record ExportOptions
{
    /// <summary>
    /// ⚠️ <b>Set by the caller, read by nobody</b> (P2-34, written down 2026-08-05).
    ///
    /// <para><c>ExportDialogViewModel.Confirm</c> fills this in from the user's choice, and
    /// <see cref="TabularExportService"/> — the only implementation — never looks at it,
    /// because the two formats produce byte-identical content (see
    /// <see cref="ExportFormat"/>). The user's choice does reach disk, but as the <b>file
    /// extension</b>, decided upstream when the path was built.</para>
    ///
    /// <para>⛔ So do not read this expecting it to steer rendering: today nothing does, and a
    /// renderer that started to would change behaviour for existing callers who set it
    /// without meaning anything by it. If a format ever renders differently, that is the
    /// moment this field acquires a meaning — and the moment to check every producer.</para>
    /// </summary>
    public ExportFormat Format { get; init; } = ExportFormat.Tsv;

    public DisplayFormat DisplayFormat { get; init; } = DisplayFormat.HexAndAscii;

    public TimestampMode TimestampMode { get; init; } = TimestampMode.Absolute;

    /// <summary>
    /// 是否输出通道列。监听会话为 true，终端会话为 false。
    ///
    /// ⚠️ <b>它同时控制 <c>Channel</c> 与 <c>Alias</c> 两列</b>（P0-7 a，2026-07-31 定）——
    /// 界面上那个勾选框叫「通道」，用户心里的「通道」就是这一整块信息。
    /// </summary>
    public bool IncludeChannelColumn { get; init; }

    /// <summary>是否输出与上一帧的时间差列。</summary>
    public bool IncludeDeltaColumn { get; init; }

    /// <summary>
    /// 通道 A / B 对应的**端口名**（如 <c>COM6</c> / <c>COM7</c>），用于 <c>Port</c> 列。
    ///
    /// ⚠️ <b>2026-08-01 起 <c>Port</c> 列取代了原先的 <c>Channel</c>（A/B）列</b>
    /// （01-spec 4.13 的连带）。原因是 <c>A</c> / <c>B</c> 在界面上已经整体退场 ——
    /// 交换 A/B 随 P0-9 删除之后，那两个字母不再指向任何用户关心的东西，
    /// **文件里留着它就成了界面上找不到对应物的一列**。
    ///
    /// <b>端口名同样满足当初选 A/B 的两条判据</b>：唯一（两路不可能选同一个口，
    /// 对话框会挡）、稳定（会话期内不变）。而它多一条好处：**自解释**。
    ///
    /// null 表示终端会话，此时通道列本就关着。
    /// </summary>
    public string? ChannelPortA { get; init; }

    /// <inheritdoc cref="ChannelPortA"/>
    public string? ChannelPortB { get; init; }

    /// <summary>
    /// 通道 A / B 的别名（如 <c>PLC</c> / <c>HMI</c>），用于 <c>Alias</c> 列。
    ///
    /// <b>为什么与端口名分成两列而不是合并</b>（P0-7 a，2026-07-31 定；2026-08-01 复核仍成立）：
    /// <list type="bullet">
    ///   <item>合并成 <c>COM6 · PLC</c> 一列会让列值变成复合字符串，
    ///         用的人还要再切一次 —— 与「一帧一行、grep 得动」那个初衷相背</item>
    ///   <item>别名可以为空、也可以两路重名；端口名不会。
    ///         **机器按 <c>Port</c> 分组，人读 <c>Alias</c>**，各取所需</item>
    /// </list>
    ///
    /// ⚠️ <b>这是本文件里唯一的一处例外</b>：别名是<b>批次级元信息</b>，
    /// 而 <see cref="IRecordingReader"/> 的注释写着「元信息刻意不写进导出文件」。
    /// 破例的理由很具体 —— <b>导出的 tsv 是独立文件</b>，
    /// 别名不进去就<b>彻底丢失</b>：半年后打开它，端口号是可推的，
    /// <b>但推不出 COM6 是 PLC 还是 HMI</b>。
    ///
    /// null 或空表示该通道没有别名，此时 <c>Alias</c> 列留空单元格。
    /// </summary>
    public string? ChannelAliasA { get; init; }

    /// <inheritdoc cref="ChannelAliasA"/>
    public string? ChannelAliasB { get; init; }
}
