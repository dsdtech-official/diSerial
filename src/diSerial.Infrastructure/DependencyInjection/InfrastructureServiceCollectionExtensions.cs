using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Decoding;
using DiSerial.Infrastructure.Diagnostics;
using DiSerial.Infrastructure.Export;
using DiSerial.Infrastructure.Formatting;
using DiSerial.Infrastructure.Recording;
using DiSerial.Infrastructure.Replay;
using DiSerial.Infrastructure.Serial;
using DiSerial.Infrastructure.Sessions;
using DiSerial.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure 层的 DI 注册入口。
///
/// 把「用哪个实现」的决策集中到这一个文件，是替换实现时唯一需要改动的位置。
///
/// 当前进度：
///   ✅ 真实实现 —— 时钟、格式化、分帧、串口 I/O、端口枚举、设备监视、
///                  <b>终端与监听会话</b>（监听的桩已于 2026-07-30 随 P0-1 退休）
///   🔶 桩实现   —— 记录器（P0-6）、导出（P0-7）
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="clock">
    /// 可选的预建时钟。日志管线在 DI 之前就需要时钟来打点，
    /// <b>必须与串口共用同一个实例</b> —— <c>MonotonicClock</c> 以构造时的墙钟为原点，
    /// 而墙钟在 Windows 上分辨率仅 15.6ms，两个实例的原点可能差出一整个刻度，
    /// 那样日志时间戳就与 <c>SerialFrame</c> 对不齐了。未提供时按原样自行创建。
    /// </param>
    /// <param name="loggerFactory">可选。未提供时全链路不产生日志，其余行为不变。</param>
    /// <param name="loggingOptions">可选。仅用于决定是否记录报文十六进制内容。</param>
    /// <param name="developerOptions">
    /// 可选。<c>debugMode</c> + <c>replay</c> 两者都开时，在端口枚举器外层叠一个
    /// <see cref="Replay.ReplayAwarePortEnumerator"/>，让 <c>REPLAY-*</c> 出现在端口列表里。
    /// 未提供时按全关处理 —— 发布构建即此路径。
    /// </param>
    public static IServiceCollection AddDiSerialInfrastructure(
        this IServiceCollection services,
        IMonotonicClock? clock = null,
        ILoggerFactory? loggerFactory = null,
        LoggingOptions? loggingOptions = null,
        DeveloperOptions? developerOptions = null)
    {
        var developer = developerOptions ?? DeveloperOptions.Disabled;
        // ---- 真实实现 ----

        // 高分辨率单调时钟。必须先于串口注册 —— 时间戳精度直接决定 Δms 是否可用。
        if (clock is not null) services.AddSingleton(clock);
        else services.AddSingleton<IMonotonicClock, MonotonicClock>();

        // 日志：未提供时注册空实现，让依赖 ILogger<T> 的服务照常解析。
        services.AddSingleton(loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(loggingOptions ?? LoggingOptions.Default);

        services.AddSingleton<IFrameFormatter, FrameFormatter>();

        // 分帧阈值：默认按串口参数自动算，可由 diserial.dev.json 的 idleGapMs 覆盖
        // （01-spec 4.8.5 的诊断出口，不是用户设置）。
        // 覆盖值在这里解析好再传进工厂 —— 与下方 ReplayConfiguration.From(developer)
        // 同一套路，让 Formatting 不必依赖 Diagnostics。
        services.AddSingleton<IFrameSplitterFactory>(
            new IdleGapFrameSplitterFactory(developer.IdleGapOverride));

        services.AddSingleton<IDecoderRegistry, DecoderRegistry>();

        // Serial I/O and enumeration: Windows goes through System.IO.Ports; macOS is
        // reported as unsupported by SerialPortFactory (a termios implementation lands
        // in V1.1).
        //
        // 若开启回放（diserial.dev.json 的 replay 键），在真实实现外套一层装饰器：
        // 端口列表追加几个虚拟端口，工厂遇到这些端口名时返回 ReplaySerialPort。
        // 真实实现本身不受影响，移除时去掉这层包装即可。
        //
        // ⚠️ 用户形态（debugMode=false）下 From() 一律返回 null ——
        // 把关在 ReplayConfiguration 内完成，这里不要再判一遍，否则判据就有两处了。
        var replayOptions = ReplayConfiguration.From(developer);
        var replayEnabled = replayOptions is not null;

        if (replayOptions is not null)
        {
            // P2-33 ②: `services.AddSingleton(replayOptions)` used to sit here and was a dead
            // registration -- the factory below captures replayOptions in its closure, and
            // nothing ever resolved ReplayOptions from the container. A registration nobody
            // reads is worse than absent: it reads as a supported way to obtain the value.
            services.AddSingleton<ISerialPortFactory>(sp => new ReplayAwareSerialPortFactory(
                new SerialPortFactory(
                    sp.GetRequiredService<IMonotonicClock>(),
                    sp.GetRequiredService<ILoggerFactory>(),
                    sp.GetRequiredService<LoggingOptions>()),
                sp.GetRequiredService<IMonotonicClock>(),
                replayOptions));

        }
        else
        {
            services.AddSingleton<ISerialPortFactory>(sp => new SerialPortFactory(
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<LoggingOptions>()));
        }

        // 端口枚举器按需叠装饰器：
        //   SystemPortEnumerator            真实端口
        //     └ ReplayAwarePortEnumerator   + REPLAY-*  （dev.json 的 replay，且受 debugMode 把关）
        // 真实实现始终保持纯粹，开发期能力全在外层，移除即去掉一层包装。
        //
        // ⚠️ 原先还有第二层 SimulatedPortEnumerator（+ SIM-A / SIM-B，只看 debugMode），
        // 2026-07-30 删除：那两个端口从不真正打开，唯一用途是配合监听桩，而桩已退休（P0-1）。
        // **于是「合成端口」现在需要两个开关都开**（debugMode + replay），
        // 不再是「开发形态就一定有假端口」—— 这一点由 ReplayPortListingTests 钉住。
        services.AddSingleton<IPortEnumerator>(sp =>
        {
            // P2-33 ①: the loggerFactory was being dropped here. Its only job is to let the
            // platform detail provider report "could not read the port description" -- a
            // degradation the user sees as a port list without descriptions, and which, without
            // this argument, could not be explained by any log line in production.
            IPortEnumerator enumerator =
                new SystemPortEnumerator(sp.GetRequiredService<ILoggerFactory>());

            if (replayEnabled) enumerator = new ReplayAwarePortEnumerator(enumerator);
            return enumerator;
        });

        // 终端与监听会话均为真实实现（监听的桩已于 2026-07-30 随 P0-1 退休）。
        services.AddSingleton<ICaptureSessionFactory, CaptureSessionFactory>();

        // 设备监视：定时轮询端口列表，依据「同一轮新增两个端口」判定双通道设备。
        // 不依赖 VID/PID，因此对任意双通道串口设备有效，且无平台特定代码。
        // 类别名 Devices —— 与 Serial.{portName} / Session / Diagnostics.* 同一风格，
        // 短且可作为过滤维度（03-conventions 8.4）。刻意不用 ILogger<PollingDeviceWatcher>：
        // 那样每行日志前面会挂一个 40 字符的全限定类名。
        services.AddSingleton<IDeviceWatcher>(sp => new PollingDeviceWatcher(
            sp.GetRequiredService<IPortEnumerator>(),
            loggerFactory?.CreateLogger("Devices")));

        // 导出：制表符分隔 + 表头 + 一帧一行（01-spec 6.4）。
        // 「导出」按钮与「停止记录后导出批次」共用它，差别只在帧从哪来。
        services.AddSingleton<IExportService, TabularExportService>();

        // 把记录批次读回成帧，供导出用。
        services.AddSingleton<IRecordingReader, SqliteRecordingReader>();

        // 落盘位置。此前只由 LoggingBootstrap 用 AppPaths.CreateDefault() 静态取用，
        // 记录库要用它，就得进容器 —— 顺带让测试能换成临时目录，
        // 不至于往用户真实的 %AppData% 里写东西。
        services.AddSingleton(AppPaths.CreateDefault());

        // 记录器按会话独立实例，由工厂直接 new，容器<b>不跟踪</b>产出物。
        //
        // ⚠️ 这里刻意不是 AddTransient<ISessionRecorder, SqliteSessionRecorder>()：
        // recorder 是 IAsyncDisposable，Transient + 从根解析会被根容器永久登记，
        // 调用方释放了也甩不掉，退出时还会被再释放一次（P0-5）。
        services.AddSingleton<ISessionRecorderFactory, SessionRecorderFactory>();

        // 发送历史（T-03a 持久化，2026-08-02 用户定）。与记录库同一个文件、另一张表。
        //
        // ⚠️ 单例，而且**不是** IDisposable —— 它每次调用自己开关连接，
        // 刻意不长持一条与 recorder 争同一个文件的连接。理由在 SqliteSendHistoryStore 的类注释里。
        services.AddSingleton<ISendHistoryStore, SqliteSendHistoryStore>();

        // 用户设置的行存储（2026-08-07 用户定，取代 settings.json）。
        //
        // ⛔ **自己一个文件，不是记录库里的一张表** —— 理由在 IAppPaths.SettingsDatabasePath：
        // 记录库只增不减且迟早会被用户删掉腾空间（P2-19），而偏好不该跟着一起没。
        //
        // ⚠️ 它只搬字符串：有哪些参数、什么类型、默认值是什么，全在 App 侧的参数目录里。
        // 那不是分层洁癖 —— 默认值一旦进了库，老库会永远带着旧默认值，
        // 而下一版改默认值将对老用户静默无效。
        services.AddSingleton<ISettingsStore, Settings.SqliteSettingsStore>();

        // ⭐ 监听会话拿的是这一个（2026-08-03 用户定）—— 注入到客户产线上的报文
        // **一个字节都不落盘**，关掉程序即消失。理由写在 IVolatileSendHistoryStore 上：
        // M-09 约束 4 规定「启用状态绝不持久化」，而载荷留在盘上与那一条互相矛盾。
        //
        // ⚠️ 单例 = 进程级（用户定）：同一次运行里关掉再开一个监听会话，历史还在 ——
        // 重敲长 HEX 载荷本身就是风险，不该为了严格而制造它。
        services.AddSingleton<IVolatileSendHistoryStore, VolatileSendHistoryStore>();

        // ---- 扩展点：协议解码器 ----
        // V1.3 在此追加，例如：
        // services.AddSingleton<IProtocolDecoder, ModbusRtuDecoder>();

        return services;
    }
}
