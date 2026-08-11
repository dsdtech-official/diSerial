using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Diagnostics;

/// <summary>
/// 开发期配置的<b>唯一来源</b>，来自可执行文件旁边的 <c>diserial.dev.json</c>。
///
/// <b>核心设计：文件随包发布，Debug 与 Release 都有，值默认全关。</b>
/// 两种构建读同一个文件、走同一条分支，差别只在文件里的值 ——
/// 消除了「发布版跑一条开发期没测过的代码路径」这一整类问题。
///
/// <code>
/// {
///   "schemaVersion": 2,
///   "debugMode": false,     // 形态选择器：false=用户形态 true=开发形态
///   "logLevel": "info",     // off|error|warning|info|debug|trace
///   "logPayload": false,    // 记报文十六进制，另需 logLevel=trace（两道门）
///   "replay": "off",        // off|on|coalesced|fragmented
///   "replayWindowMs": 16,   // 攒帧窗口，仅 coalesced 有效
///   "idleGapMs": null       // 分帧阈值覆盖。缺失=自动 0=每块一行 正数=该毫秒值
/// }
/// </code>
///
/// ⚠️ <b><see cref="DebugMode"/> 的语义：会不会让界面显示非真实内容</b>。
/// 它决定模拟端口、回放是否生效、Avalonia DevTools 是否启用，
/// 外加把关 <see cref="LogPayload"/>（客户报文只在开发形态下才可能落盘）。
///
/// ⚠️ <b>它不决定日志记多细</b> —— 详细度归 <see cref="LogLevel"/>，两者刻意正交。
/// 原先 01-spec 4.7 有三条「开发形态额外」的日志条款，2026-07-29 核对代码后确认
/// <b>它们从来就是两种形态一样的</b>，已并入「两种形态共有」。
///
/// 原名 <c>simulateDeviceInsertion</c> 只描述了其中一个效果，2026-07-28 改为现名，
/// <see cref="CurrentSchemaVersion"/> 随之升到 2。
///
/// 几条刻意的取舍：
///
/// <list type="number">
///   <item><b>放输出目录、不放用户数据目录</b> —— 开发配置的作用域是「这一个构建」。
///         放 <c>%AppData%</c> 会跨安装、跨版本一直生效，几个月后没人记得
///         为什么界面上有模拟端口。</item>
///   <item><b>不用 <c>#if DEBUG</c> 隔离功能代码</b> —— 条件编译会让 Release 变成
///         一条测试较少的代码路径。全项目已无任何 <c>#if DEBUG</c>。</item>
///   <item><b>缺的键用代码默认值</b>（稀疏覆盖）—— 否则以后新增开关，
///         所有人现有的 dev 文件都得跟着更新。因此新增键<b>不必</b>升 schemaVersion。</item>
/// </list>
///
/// ⚠️ <b>A corrupt file here is never silent</b>: it logs a Warning. The audience is a
/// developer, and silently falling back would leave someone staring at "why did my switch
/// not take effect".
///
/// <para>⛔ <b>This paragraph used to justify itself by contrast</b> — "unlike
/// <c>settings.json</c>, which is user data and degrades silently". ⚠️ <b>That contrast
/// stopped being true on 2026-08-07</b> (P2-77): user settings moved to <c>settings.db</c>,
/// where an unreadable store is logged at <b>Error</b> and the bad file is quarantined. So
/// the user-data side is now <i>louder</i> than this one, and the two policies differ by
/// severity rather than by silence. ⭐ <b>The rule above did not change; only the reason
/// given for it was wrong.</b> Found by P2-82.</para>
///
/// ⚠️ <b>发布包里带着这个文件，意味着用户可以手改它。</b>
/// 这是明确接受的代价，兜底是：任一开关开启时标题栏持续显示「开发模式」。
/// 配套假设 ——「发布时不会把 <c>debugMode: true</c> 提交上去」——
/// 见 docs/02-architecture.md 11.3 ②。
/// </summary>
public sealed record DeveloperOptions
{
    /// <summary>配置文件名。位于 <see cref="AppContext.BaseDirectory"/> 下。</summary>
    public const string FileName = "diserial.dev.json";

    /// <summary>
    /// 本代码能理解的最高格式版本。
    ///
    /// <b>2 起于 2026-07-28</b>：<c>simulateDeviceInsertion</c> 改名为 <c>debugMode</c>。
    /// 这正是本字段存在的唯一场景 —— 旧文件仍声明 1，读到时会记一条 Warning，
    /// 提示「有键没被认出来」，而不是让开关静默失效。
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// 文件格式版本。文件里没写时归一为 <see cref="CurrentSchemaVersion"/>。
    ///
    /// 用途只有一个：<b>改键名时不至于静默失效</b>。新增键靠稀疏覆盖即可兼容，
    /// 但重命名会让旧键被无声忽略 —— 开发者会对着「开关怎么不生效」查半天。
    /// 版本对不上时记 Warning，仍按能解析的部分继续。
    ///
    /// ⚠️ 归一在 <see cref="Load"/> 里显式做，<b>不依赖属性初始化器</b> ——
    /// 实测源生成的反序列化路径不会执行初始化器，缺省会得到 0。
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// <b>形态选择器</b>：<c>false</c> = 用户形态，<c>true</c> = 开发形态。
    ///
    /// 开发形态包含用户形态的全部行为，<b>另加两条</b>（01-spec 4.7）：
    ///   1. <b>把关 <see cref="Replay"/></b> —— 两者都开时 <c>REPLAY-*</c> 才进端口列表
    ///   2. Avalonia DevTools 启用
    ///
    /// ⚠️ <b>本项自己不再产生任何合成端口</b>（2026-07-30 起）。原先它会让
    /// <c>SIM-A</c> / <c>SIM-B</c> 进入端口列表并启用插入提示；提示随 M-02
    /// 于 2026-07-29 移除，那两个端口随监听桩于 2026-07-30 删除
    /// （它们从不真正打开，唯一用途就是配合那个桩）。
    /// <b>于是「界面上出现假数据」现在需要 <c>debugMode</c> + <c>replay</c> 两个开关都开。</b>
    ///
    /// 第 1 条会让界面显示非真实内容 —— 这正是标题栏那句「开发模式 — 数据可能为模拟」
    /// 所警告的东西。<b>异常记录的详细程度不在此列</b>，那归 <see cref="LogLevel"/>。
    ///
    /// 另外它还把关 <see cref="LogPayload"/>，但那是「不让客户报文在用户形态下落盘」，
    /// 与「额外行为」是两回事。
    ///
    /// JSON 键为 <c>debugMode</c>（原 <c>simulateDeviceInsertion</c>，
    /// 那个名字只描述了其中一个效果）。
    /// </summary>
    public bool DebugMode { get; init; }

    /// <summary>
    /// 日志级别：<c>off</c> / <c>error</c> / <c>warning</c> / <c>info</c> / <c>debug</c> / <c>trace</c>。
    /// 为 null（键缺失）时按 <c>info</c> 处理。
    ///
    /// ⚠️ <b>与形态正交，而且必须正交</b>：日志默认不对用户开放（01-spec 4.7 共有第 4 条），
    /// 出问题时由支持途径指导用户取回 —— 那条路径的典型一句是
    /// 「请把 logLevel 改成 debug 再复现一次」。<b>这要求它在用户形态下必须有效。</b>
    ///
    /// <b>日志详细度只归本项管</b>，<see cref="DebugMode"/> 不参与 ——
    /// 用形态再管一遍音量就是两套机制管同一件事。
    /// </summary>
    public string? LogLevel { get; init; }

    /// <summary>
    /// 是否允许把报文十六进制记入日志。
    ///
    /// ⚠️ <b>三个条件缺一不可</b>（2026-07-29 起）：
    /// <see cref="DebugMode"/> 为 true、<see cref="LogLevel"/> 为 <c>trace</c>、本项为 true。
    ///
    /// <b>为什么由 <see cref="DebugMode"/> 把关</b>：用户形态下日志默认不对用户开放
    /// （见 01-spec 4.7），那时记录客户现场的总线数据没有任何用途，只有泄漏风险。
    /// 把关之后「用户形态永远不会记录客户报文」从一条纪律变成机械保证 ——
    /// 与 <see cref="Replay"/> 的处理方式一致。
    ///
    /// ⚠️ <b>把关 ≠ 自动打开</b>：开发形态下这两道门仍要各自显式打开，
    /// 详细异常记录默认不含报文内容。两条护栏分别钉住这两件事，见 LoggingOptionsTests。
    /// </summary>
    public bool LogPayload { get; init; }

    /// <summary>
    /// 回放端口：<c>off</c> / <c>on</c> / <c>coalesced</c> / <c>fragmented</c>。
    /// 为 null（键缺失）时按 <c>off</c> 处理。
    ///
    /// ⚠️ <b>仅在 <see cref="DebugMode"/> 为 true 时生效</b> ——
    /// 回放端口走的是真实采集链路，产出的帧在日志里与真硬件的帧无法区分。
    /// 不把关，「用户形态不产生模拟数据」就只是一句空话。
    ///
    /// <c>coalesced</c> 刻意保留：它是没有硬件时唯一能预演「驱动攒帧」的手段。
    /// </summary>
    public string? Replay { get; init; }

    /// <summary>
    /// 攒帧窗口毫秒数，仅 <c>coalesced</c> 有效。0 或负数表示用内置默认值。
    /// </summary>
    public int ReplayWindowMs { get; init; }

    /// <summary>
    /// 空闲分帧阈值的覆盖，毫秒。<b>C-07 的诊断出口</b>（规格见 docs/01-spec.md 4.8.5）。
    ///
    /// <list type="table">
    ///   <item><term>键缺失 / null</term><description>按串口参数自动算（默认）</description></item>
    ///   <item><term><c>0</c></term><description><b>每块一行</b> —— 判据恒真，每块立即收尾。即「不分帧」</description></item>
    ///   <item><term>正数</term><description>覆盖成该毫秒值</description></item>
    /// </list>
    ///
    /// ⚠️ <b><c>0</c> 的语义与 <see cref="ReplayWindowMs"/> 的 <c>0</c> 相反</b>
    /// （后者是「用内置默认值」）。这是个真实的陷阱，dev.json 里两处注释都点明了。
    /// 之所以不统一：本键需要区分「缺失」与「0」三态，而那正好落在本文件
    /// 「缺的键用代码默认值」的稀疏覆盖规则上。
    ///
    /// ⚠️ <b>为什么是开发者配置而不是用户设置</b>：C-07 的规格是「唯一策略 + 零用户旋钮」。
    /// 但零旋钮不等于零观测能力 —— 现场撞上分帧异常时，若连「驱动到底给了什么块」
    /// 都看不到，就无从判断是设备、驱动，还是阈值不合。
    /// 与 <see cref="LogLevel"/> 同一性质：<b>诊断旋钮、与形态正交、在已收敛的这一份配置里。</b>
    ///
    /// <b>用 <c>double</c> 而不是 <c>int</c></b>：自动算出的阈值本身就是小数
    /// （9600 8N1 = 3.646ms，高波特率钳到 1.75ms），整数无法表达同一量级的覆盖值。
    /// </summary>
    public double? IdleGapMs { get; init; }

    /// <summary>
    /// 解析后的阈值覆盖；<c>null</c> 表示「按串口参数自动算」。
    ///
    /// 负数按缺失处理 —— 与 <see cref="LogLevel"/> 写错时退回 <c>info</c> 同一个原则：
    /// <b>取值写错一律退回安全默认，不抛异常。</b>
    /// </summary>
    public TimeSpan? IdleGapOverride =>
        IdleGapMs is { } ms and >= 0 ? TimeSpan.FromMilliseconds(ms) : null;

    /// <summary>
    /// 供日志与启动横幅使用的一句话描述。
    ///
    /// <b>必须留痕</b>：本键直接改变界面上「一行是什么」—— 覆盖成 0 时每块一行，
    /// 屏幕上的分行与默认状态完全不同。不记它，拿到日志的人会拿着一份
    /// 按非默认阈值切出来的帧去推断驱动行为。与 <see cref="Replay"/> 同一个道理
    /// （原 P2-7 的教训：能改变屏幕内容的配置不能在诊断上无痕）。
    ///
    /// 英文、不进资源文件 —— 只出现在日志里，按 03-conventions 2.1 的判据。
    /// </summary>
    public string IdleGapDescription => IdleGapMs switch
    {
        null => "auto",
        < 0 => "auto (negative override ignored)",
        0 => "0 (every chunk becomes one frame)",
        { } ms => ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "ms"
    };

    /// <summary>全部关闭。文件不存在或无法读取时的取值。</summary>
    public static DeveloperOptions Disabled { get; } = new();

    // 这里曾有一个聚合属性 IsAnyEnabled（其实现就是 => DebugMode），已于 2026-07-28 删除。
    //
    // 它诞生于「将来会有多个开发开关」的假设，而那个假设没有成立：
    // Replay 被 DebugMode 把关、LogLevel 只是音量 —— 最终只有 DebugMode 一个开关
    // 决定「界面要不要显示开发模式标识」。于是它变成了 DebugMode 的同义词，
    // 而名字里的「Any」仍在暗示有多个开关，读代码的人会去找那些不存在的东西。
    // 调用点现在直接用 DebugMode。
    //
    // ⚠️ 若将来真加了第二个「会让界面显示假数据」的开关，那时再引入聚合属性，
    // 并给它一个说得清的名字（例如 ShowsSimulatedData）。

    /// <summary>
    /// 从可执行文件所在目录读取配置。
    ///
    /// <b>文件不存在是正常情况</b>（发布构建即如此），不记任何日志。
    /// 只有文件存在却读不懂时才告警。
    /// </summary>
    /// <param name="directory">覆盖查找目录，仅供测试使用。</param>
    public static DeveloperOptions Load(string? directory = null, ILogger? logger = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);

        if (!File.Exists(path)) return Disabled;

        try
        {
            var json = File.ReadAllText(path);

            // 空文件视为「全部默认」，不算错误 —— 建个空文件占位是常见做法。
            if (string.IsNullOrWhiteSpace(json)) return Disabled;

            var parsed = JsonSerializer.Deserialize(json, DeveloperOptionsContext.Default.DeveloperOptions);
            if (parsed is null) return Disabled;

            // 文件里没写 schemaVersion 时归一为当前版本（缺省会反序列化成 0）。
            if (parsed.SchemaVersion == 0) parsed = parsed with { SchemaVersion = CurrentSchemaVersion };

            if (parsed.SchemaVersion != CurrentSchemaVersion)
            {
                // 不拒绝加载 —— 能认出多少算多少。但必须说出来，
                // 否则改过键名之后开关静默失效，很难联想到是版本问题。
                logger?.LogWarning(
                    "{Path} declares schemaVersion {Actual}, this build understands {Expected}. "
                    + "Unrecognised keys are ignored.",
                    path, parsed.SchemaVersion, CurrentSchemaVersion);
            }

            // 区分「文件读到了但没开任何开关」与「确实开了」。
            //
            // 这两种情况必须说得不一样：前者最常见的成因是复制了模板却忘了改值，
            // 若也报「数据可能是模拟的」，看日志的人反而会以为开关已经生效，
            // 于是绕开真正的原因去别处找。
            if (!parsed.DebugMode)
            {
                // 顺带报出 replay：它在用户形态下被把关忽略，
                // 若有人设了却不生效，这一句是他唯一的线索。
                // ⚠️ idleGap 也报出来：它与形态正交（用户形态下同样生效），
                // 且直接改变界面上的分行。见 IdleGapDescription 的注释。
                logger?.LogInformation(
                    "Developer options file found at {Path}, but debugMode=false. "
                    + "Running in USER FORM: no simulated data. "
                    + "logLevel={Level}, idleGap={IdleGap}, replay={Replay} (ignored in user form).",
                    path, parsed.LogLevel ?? "info", parsed.IdleGapDescription, parsed.Replay ?? "off");
            }
            else
            {
                logger?.LogWarning(
                    "Developer options ACTIVE from {Path}: DEVELOPER FORM. "
                    + "debugMode=true, replay={Replay}, logLevel={Level}, idleGap={IdleGap}. "
                    + "Displayed traffic may be SIMULATED.",
                    path, parsed.Replay ?? "off", parsed.LogLevel ?? "info", parsed.IdleGapDescription);
            }

            return parsed;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // 开发者改坏了文件。告警而非静默 —— 见类型文档注释。
            logger?.LogWarning(e,
                "Failed to read {Path}; developer options are disabled for this run.", path);
            return Disabled;
        }
    }
}

/// <summary>源生成的序列化上下文，避免反射，保持 Trimming / NativeAOT 友好。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DeveloperOptions))]
internal sealed partial class DeveloperOptionsContext : JsonSerializerContext;
