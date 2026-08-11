using DiSerial.Core.Models;

namespace DiSerial.App.Services;

/// <summary>
/// The in-memory shape of the user's settings. Persisted one parameter per row via
/// <see cref="SettingsCatalog"/>; the storage seam itself is <c>ISettingsStore</c>.
///
/// <para>修改走 <c>with</c> 表达式整体替换某一节，一次赋值即触发持久化
/// （见 <see cref="IAppSettings"/>）。</para>
///
/// <para>⚠️ <b>这里原先有一段「属性必须是 <c>set</c> 而不是 <c>init</c>」的强约束
/// （2026-08-07 前）</b>，理由是 <c>System.Text.Json</c> 的
/// <c>JsonObjectCreationHandling.Populate</c> 填不了 <c>init</c> 属性，于是老文件里缺失的键
/// 会全部变成 <c>0</c> / <c>false</c> / <c>null</c>。⛔ <b>那个理由随 JSON 一起没了</b> ——
/// 现在每一行单独套用，缺的行就是「用本 build 的默认值」，而 <c>with</c> 表达式对
/// <c>init</c> 与 <c>set</c> 一样有效。<b>属性保持 <c>set</c> 只是没必要动它</b>，
/// 不再是一条承重约束。</para>
///
/// <para>⚠️ 一个仍然成立的代价：这些 record 可变，存在
/// <c>settings.Terminal.Serial.BaudRate = 9600</c> 这种「能编译但不会存盘」的写法 ——
/// 正确写法始终是整节赋值。</para>
///
/// <para>⛔ <b>没有 schema 版本字段了。</b> 它原先是文件格式版本；现在版本记在 SQLite 自己的
/// <c>user_version</c> 里（<c>SqliteSettingsStore.SchemaVersion</c>），不再是模型的一部分 ——
/// 模型描述的是「有哪些设置」，不是「文件长什么样」。</para>
/// </summary>
public sealed record AppSettingsModel
{
    /// <summary>界面语言（如 <c>zh-Hans</c>）。null 表示从未选择过，走默认英语。</summary>
    public string? Language { get; set; }

    /// <summary>
    /// 上一次**导出成功**到的目录（P33，2026-08-10 用户提）。null 表示还没成功导出过。
    ///
    /// <para>⚠️ <b>只存目录，不存完整路径</b>：文件名每次都由会话与时间重新生成
    /// （<c>diserial-COM5-9600-8N1-…</c>），存进来下一次也会被盖掉。</para>
    ///
    /// <para>⛔ <b>写入点是「导出真的成功了」，不是「点了确定」</b> —— 用户定。
    /// 点取消、或写盘失败，都不该改变下一次的默认位置。</para>
    ///
    /// <para>⚠️ <b>读的时候必须先确认它还在</b>：U 盘拔了、文件夹删了、换了台机器，
    /// 这个路径就指向不存在的地方。判据与回落写在
    /// <c>SessionViewModel.ResolveExportDirectory</c> 上。</para>
    /// </summary>
    public string? LastExportDirectory { get; set; }

    public SessionPreferences Terminal { get; set; } = SessionPreferences.TerminalDefaults;

    public SessionPreferences Monitor { get; set; } = SessionPreferences.MonitorDefaults;

    // ⚠️ MonitorSyncParameters ("sync parameters", M-03) was removed here on 2026-08-02 (P1-49).
    // This is the one place that carries the full reason; everything else points back here.
    //
    // **It was never wired up - not once, from the first commit.** Two checkboxes (session
    // panel + new-session dialog) toggled it, the value round-tripped through four copies and
    // was persisted here, and no code anywhere ever read it to change behaviour.
    // ISerialPort.ApplySettingsAsync, the port left for it, was deleted the same day for the
    // same reason: zero callers since the first commit, and no document ever said why it
    // existed. Judged separately from this, because deleting M-03 did not authorise it.
    //
    // **Why deleting beats implementing it.** M-03 existed to save the user from entering the
    // same serial parameters twice. But ICaptureSessionFactory.CreateMonitor takes a *single*
    // SerialPortSettings and the dialog has a *single* PortSettingsViewModel - so "both sides
    // share one set of parameters" is welded into the structure and the saving is already
    // free. The checkbox had nothing to switch: turning it off would need a second parameter
    // panel, CreateMonitor taking two settings, and per-channel state in the session.
    //
    // ⚠️ **Why it is worth this many lines**: this is the nastiest shape of "what you see in
    // the UI is not what exists" the project has hit - the control was visible, clickable,
    // reacted to clicks, and even remembered its state across restarts. From the user's side
    // it looked like it worked. See 00-STATUS P1-49 and 03-conventions.
    //
    // A leftover "monitorSyncParameters" key in an old settings.json needs no migration code:
    // Populate mode ignores unknown keys and the next whole-file write drops it. Same path the
    // deleted "send" section took on 2026-07-31 (see the note at the bottom of this file).
    //
    // ⚠️ If "two ports NOT on the same bus, each with its own parameters" is ever wanted, it
    // is a **new feature** that goes through 01-spec first - not "fixing M-03".
}

/// <summary>一种会话类型的全部偏好。终端与监听各存一套。</summary>
public sealed record SessionPreferences
{
    public SerialPreferences Serial { get; set; } = new();

    public DisplayPreferences Display { get; set; } = new();

    /// <summary>
    /// Terminal session display defaults: relative timestamps and the direction column,
    /// no delta column.
    ///
    /// <para>⚠️ <b><c>ShowChannel</c> went from false to true on 2026-07-29.</b> It was
    /// false, and <b>no control anywhere could turn it on</b> -- every <c>.axaml</c> bound
    /// it through <c>IsVisible</c> only, not one of them wrote the value. So the direction
    /// markers on a terminal session (<c>← RX</c> / <c>→ TX</c>, computed by
    /// <c>FrameViewModel</c>) were <b>permanently invisible to the user</b>, and there was
    /// no way to reach them.</para>
    ///
    /// <para>⛔ The original comment ended "unless you hand-edit settings.json", and that
    /// escape hatch is gone twice over: settings live in <c>settings.db</c> since
    /// 2026-08-07 (P2-77), and <b>hand-editing was explicitly ruled out by the user</b> --
    /// that ruling is the premise the whole SQLite decision rests on. A preference with no
    /// write binding is now simply unreachable, which is what
    /// <c>PreferenceWriteBindingTests</c> exists to catch.</para>
    ///
    /// <para><see href="01-spec.md">01-spec</see> 8.3 lists "direction marker: receive /
    /// send (local view)" as a terminal session UI element, so it is a <b>promise</b>.</para>
    ///
    /// <para>This defect was undiscoverable before a virtual serial port was connected:
    /// terminal sessions had only ever run against REPLAY ports, which are <b>all receive
    /// and no send</b> -- a column of nothing but RX does not look like it is missing
    /// anything.</para>
    /// </summary>
    public static SessionPreferences TerminalDefaults => new()
    {
        Display = new DisplayPreferences
        {
            Timestamp = TimestampMode.Relative,
            ShowChannel = true,
            ShowDelta = false
        }
    };

    /// <summary>
    /// 监听会话的默认显示偏好：绝对时间戳 + 通道列 + 增量列。
    /// 合并时间轴上没有这两列就看不出「谁在什么时候说话」。
    /// </summary>
    public static SessionPreferences MonitorDefaults => new()
    {
        Display = new DisplayPreferences
        {
            Timestamp = TimestampMode.Absolute,
            ShowChannel = true,
            ShowDelta = true
        }
    };
}

/// <summary>
/// 串口参数的持久化形式。
///
/// ⚠️ <b>刻意不直接序列化 <see cref="SerialPortSettings"/></b>：那个 record 上有
/// <c>ShortDescription</c> 与 <c>DefaultIdleGap</c> 两个计算属性，
/// <c>System.Text.Json</c> 会把它们当成公开 getter 一并写进文件，
/// 读回来时又因为只读而被忽略 —— 于是文件里出现两个
/// <b>看起来能改、改了却没用</b>的字段。手工编辑过 <c>defaultIdleGap</c>
/// 之后发现无效，是很难自己想明白的。
///
/// 分开一层还有个好处：领域模型加字段不会自动改变文件格式。
///
/// ⚠️ <b>不含端口名</b>。USB 转串口设备的端口名不稳定 —— 换个 USB 口、
/// 或先插别的设备，<c>COM3</c> 就可能变成 <c>COM7</c>。
/// 把配置挂在不稳定标识上，会在最需要它的现场恰好失效。
/// 这是「设备识别不用 VID/PID」（架构原则 3）同一判断的延伸。
/// </summary>
public sealed record SerialPreferences
{
    // 默认值取自领域模型，不在此另写一份 —— 两处各写一遍迟早会漂移。
    private static readonly SerialPortSettings CoreDefaults = new();

    public int BaudRate { get; set; } = CoreDefaults.BaudRate;

    public int DataBits { get; set; } = CoreDefaults.DataBits;

    public SerialParity Parity { get; set; } = CoreDefaults.Parity;

    public SerialStopBits StopBits { get; set; } = CoreDefaults.StopBits;

    public SerialFlowControl FlowControl { get; set; } = CoreDefaults.FlowControl;

    public SerialPortSettings ToSettings() => new()
    {
        BaudRate = BaudRate,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        FlowControl = FlowControl
    };

    public static SerialPreferences From(SerialPortSettings settings) => new()
    {
        BaudRate = settings.BaudRate,
        DataBits = settings.DataBits,
        Parity = settings.Parity,
        StopBits = settings.StopBits,
        FlowControl = settings.FlowControl
    };
}

/// <summary>
/// 显示区偏好（C-05 / C-06）。
///
/// ⚠️ <b>不含 <c>AutoScroll</c> 与 <c>IsPaused</c></b>：那两个是瞬时状态而非偏好。
/// 存下来会出现「启动后不滚动，用户以为坏了」—— 每次启动都应当是跟随末尾。
/// </summary>
public sealed record DisplayPreferences
{
    public DisplayFormat Format { get; set; } = DisplayFormat.HexAndAscii;

    public TimestampMode Timestamp { get; set; } = TimestampMode.Absolute;

    public bool ShowChannel { get; set; }

    public bool ShowDelta { get; set; }

    /// <summary>
    /// 是否显示本机发出的数据（T-05，01-spec 4.12）。
    ///
    /// ⚠️ <b>默认 <c>true</c> —— 与本记录里另外两个 <c>bool</c> 相反。</b>
    /// 2026-08-07 前这里写着一段关于「旧 settings.json 缺这个键会不会拿到 `false`」的分析；
    /// ⛔ **那个担心随逐行存储消失了** —— 缺的行就是「用属性初始化器给的值」，
    /// 也就是 `true`，没有任何一步会先把对象清成默认值。
    ///
    /// <b>为什么它可以持久化，而发送区那两项不行</b>（见下方那段注释）：
    /// 它**只改看得见什么，不改线上出现什么字节**。上次藏了 TX 这次还藏着，
    /// 最坏是用户少看见几行，而那几行一勾就回来。
    ///
    /// ⚠️ <b>监听会话那一格（P21）有一处开着的问题</b>：勾选框
    /// （<c>SendPanelView.axaml</c>）绑的是 <c>!IsMonitorSession</c>，**监听会话里根本不显示**，
    /// 而 P21 照样持久化 —— 一个存得下、却在那种会话里改不了的偏好。
    /// <c>PreferenceWriteBindingTests</c> 看不见这一层（它按文件扫，不分会话类型，
    /// 而终端那个勾选框让它绿）。见 00-STATUS 的条目，别当它是有意设计。
    /// </summary>
    public bool ShowSent { get; set; } = true;
}

// ⚠️ 这里原先有一个 SendPreferences（hexMode + lineEnding），2026-07-31 整节删除。
//
// **发送区的两项一律不记**，理由与 M-09 那三项发送开关刻意不进本文件是同一条：
// 它们**改变的是线上真实出现的字节**，而「上次是什么这次就是什么」
// 对这一类开关是不可接受的默认 —— 上次留下的 CRLF 会在下次给 Modbus 设备
// 多发两个字节，CRC 直接错，而用户并没有做任何操作。
//
// 每次启动回到 ASCII + 无结束符，是**安全的**那一侧。
// 代价明知：常发文本命令的用户每次要重选一次结束符（01-spec 4.5 写着）。
//
// ⛔ 那一节现在连「被忽略」都谈不上：它从来没有过 P 编号，所以在参数表里根本不存在。
// ⭐ 这正是逐行存储与整份文件的差别 —— 老结构不需要「被容忍」，它只是没有那几行。

// ⚠️ 这里原先还有一个源生成的 JSON 序列化上下文（AppSettingsContext），2026-08-07 随
// settings.json 一起删除。它当时承担四件事，现在各有去处，记下来是因为其中两件**没有**
// 自动继承过来：
//
//   1. 枚举写成字符串而非数字        -> SettingsCatalog 仍然存 ToString()，理由不变：
//                                       枚举成员重排时数字形式会静默改变含义。
//   2. 稀疏覆盖（Populate）          -> 由「从默认模型起，逐行套用」取代，见 StoredAppSettings.Load。
//   3. 键名大小写不敏感              -> ⛔ 没有了，也不需要：键是 P 编号，不是人打出来的名字。
//   4. Trimming / NativeAOT 友好     -> ⛔ 不再由源生成保证。现在这条路上没有反射序列化，
//                                       目录里是显式的委托，所以它是**结构上**成立的。
