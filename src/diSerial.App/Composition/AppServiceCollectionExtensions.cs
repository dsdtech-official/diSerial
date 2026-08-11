using System.Globalization;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.Composition;

/// <summary>
/// 组合根（Composition Root）—— 整个应用唯一装配具体实现的位置。
///
/// 除本文件外，任何代码都只依赖接口。因此替换实现（补齐真实串口、
/// V1.1 增加 macOS 支持、V1.3 挂载 Modbus 解码器、更换多语言后端）时，
/// 改动范围可控。
/// </summary>
public static class AppServiceCollectionExtensions
{
    /// <summary>
    /// 装配 + 建容器。应用启动走这一条。
    /// </summary>
    public static IServiceProvider BuildAppServices()
    {
        // ⚠️ 这两个自检**与 debugMode 无关，刻意无条件开启**（2026-07-28 明确决定）。
        //
        // ValidateOnBuild：建容器时遍历每一条注册，逐个验证能不能构造出来。
        //                  有问题在启动时一次列全，而不是等到第一次真去解析它才炸。
        // ValidateScopes ：禁止从根容器解析 scoped 服务，防「捕获依赖」。
        //
        // **为什么不挂到 debugMode 后面**：它们是 fail-fast 机制，不是诊断音量。
        // 挂上去意味着发布版跳过验证 —— Debug 里启动就报错的接线问题，
        // 到了 Release 会变成用户操作到一半时的一个费解异常。
        // 那正是这一轮改造要消灭的那类问题（发布版跑一条开发期没测过的路径）。
        //
        // 成本可忽略：三四十条注册，启动时几毫秒，一次性。
        //
        // ⚠️ ValidateScopes 目前**什么也没守住** —— 全项目零个 AddScoped / CreateScope。
        // 留着是给将来引入 scope 时买的保险。另外它有已知盲区：
        // 抓不到「Transient 的 IDisposable 从根解析」那种泄漏（P0-5 就是它放过去的），
        // 那一类改由 CompositionRootTests 扫注册表守着。
        return CreateAppServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    /// <summary>
    /// Registers services without building a container.
    ///
    /// <para>Split out so the guardrail tests can <b>scan the registrations themselves</b>
    /// (<c>CompositionRootTests</c>). Going through <see cref="BuildAppServices"/> and then
    /// resolving would not do: that pulls in <c>StoredAppSettings</c>, and the test would
    /// start reading and writing the user's real settings database.</para>
    ///
    /// <para>⚠️ This used to name <c>JsonAppSettings</c> and <c>settings.json</c>, both of
    /// which stopped existing on 2026-08-07 (P2-77 replaced the store with
    /// <c>settings.db</c>). The comment kept compiling and kept reading as true, which is
    /// the whole shape of P2-82.</para>
    /// </summary>
    public static IServiceCollection CreateAppServices()
    {
        var services = new ServiceCollection();

        // 基础设施层（Core 各接口的具体实现）。
        //
        // 时钟由日志管线在 DI 之前就已建好，此处必须复用同一个实例 ——
        // 否则日志时间戳与 SerialFrame 会各自锚定在不同的墙钟原点上而对不齐。
        var logging = LoggingBootstrap.Current;
        var developer = logging.Developer;

        services.AddDiSerialInfrastructure(
            logging.Clock, logging.LoggerFactory, logging.Options, developer);

        // 开发者开关（diserial.dev.json，文件不存在即全关）。
        // 在组合根这一处把 Infrastructure 的 DeveloperOptions 映射为 App 层的
        // DeveloperModeState —— ViewModel 只认后者，不直接引用 Infrastructure。
        services.AddSingleton(new DeveloperModeState(developer.DebugMode));

        // ⚠️ 原先这里在 debugMode 下注册 IDeviceSimulator，用来伪造一次双通道接入事件、
        // 触发插入提示。插入提示整个移除后（2026-07-29），这段注册与接口一并删掉。
        //
        // debugMode 现在对端口的效果是**把关 replay** —— 两者都开时 REPLAY-* 才进端口列表
        // （ReplayAwarePortEnumerator 装饰器，在 Infrastructure 侧注册）。
        //
        // ⚠️ 原先还有 SIM-A / SIM-B（只看 debugMode），2026-07-30 一并删除：
        // 它们从不真正打开，唯一用途是配合监听桩，而桩已随 P0-1 退休。
        // **无硬件时要看监听会话，用两个 REPLAY-* 端口** —— 走真实采集链路，比桩更可信。

        // ---- 多语言（C-25）----
        // 后端为 RESX；若将来改用 JSON 或第三方库，只需替换此处的实现，
        // 全部调用点依赖的都是 ILocalizationService。
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IEnumChoiceProvider, EnumChoiceProvider>();
        // 用户设置（settings.db，2026-08-07 起；此前是 settings.json）。
        // 赋值即持久化 —— 调用点不决定何时存盘。
        //
        // ⚠️ 拿到的是 ISettingsStore（Core 的抽象），而不是 Infrastructure 的具体类型 ——
        // 存储实现由 AddDiSerialInfrastructure 注册，路径由它自己从 IAppPaths 取。
        // ⭐ **这一层比原先更干净了**：原先要把 ConfigDirectory 拆成字符串传进 App/Services，
        // 才不让 Infrastructure 的类型泄漏过去；现在 App/Services 只认识一个 Core 接口。
        services.AddSingleton<IAppSettings>(sp => new StoredAppSettings(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetService<ILogger<StoredAppSettings>>()));

        // 解码摘要的落地实现。Infrastructure 刻意不自带注册 ——
        // App 层忘了提供时应当在 ValidateOnBuild 阶段立刻失败，
        // 而不是让界面上悄悄显示一堆资源键名。
        services.AddSingleton<ILocalizedTextResolver, LocalizedTextResolver>();

        // 表现层服务
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISessionViewModelFactory, SessionViewModelFactory>();

        // The one place that knows repeating ticks come from a dispatcher. Everything else
        // depends on IPeriodicTimer so it stays drivable from tests with no Avalonia runtime
        // -- see IPeriodicTimer for the false green this prevents.
        services.AddSingleton<IPeriodicTimerFactory, DispatcherPeriodicTimerFactory>();

        services.AddSingleton<SessionContext>();

        // ⚠️ ISessionRecorder 在此**没有注册** —— 它由 ISessionRecorderFactory
        // 直接 new 出来（Infrastructure 侧注册）。原先这里有一条
        // AddSingleton<Func<ISessionRecorder>>(sp => sp.GetRequiredService<ISessionRecorder>)，
        // 那正是 P0-5 的泄漏源，理由写在 ISessionRecorder.cs 的工厂接口注释里。

        // ViewModel
        services.AddSingleton<MainWindowViewModel>();

        // 对话框 ViewModel 每次打开新建一个，用完由调用方 using 释放
        // （MainWindowViewModel 的两个创建点）。
        //
        // ⚠️ 用 ActivatorUtilities 而不是 sp.GetRequiredService —— 与上面
        // recorder 是同一个道理：NewSessionDialogViewModel 经 LocalizedViewModelBase
        // 实现 IDisposable，走容器解析会被根容器永久跟踪。ActivatorUtilities
        // 照样从 sp 取构造参数，但产出物不进任何释放列表。
        // 会话类型目录 —— 新建会话对话框的类型清单，也是**唯一**知道有哪些具体
        // 会话类型的地方（2026-08-03）。新增类型时只加这里一条 + 一个配置 VM +
        // 一个配置 View，对话框本身一行不改（NewSessionDialogDecouplingTests 守着）。
        services.AddSingleton<ISessionTypeCatalog, SessionTypeCatalog>();

        services.AddSingleton<Func<NewSessionDialogViewModel>>(sp =>
            () => ActivatorUtilities.CreateInstance<NewSessionDialogViewModel>(sp));

        // 视图定位器（按命名约定分发 DataTemplate）
        services.AddSingleton<ViewLocator>();

        return services;
    }

    /// <summary>
    /// Applies the UI language saved last time.
    ///
    /// <para>Must run <b>before</b> any ViewModel is constructed — session titles and the
    /// status bar are computed properties evaluated for the first time in their constructors,
    /// so setting the language later makes the first frame render in English and then jump.
    /// With nothing ever chosen, the default English stays.</para>
    ///
    /// <para>⭐ <b>It logs the language that actually ended up in effect</b> (P1-28, option C).
    /// The startup banner carries a <c>culture.ui</c> field, and that field <b>lies</b>: the
    /// banner is written from <c>Program.Main</c>, before Avalonia even starts, so it reports
    /// the process default from before this method runs. Someone reading a user's log saw
    /// <c>culture.ui=en-US</c> next to a Chinese screenshot and had no way to reconcile them.
    /// </para>
    ///
    /// <para>⛔ <b>Why not simply move the banner later</b>: the banner's value is that it is
    /// early — it has to precede startup-phase crashes (03-conventions 8.2 ③). "Which language
    /// actually got used" is a separate fact and gets its own line, in the same
    /// <c>Diagnostics.Startup</c> category so a reader finds it next to the banner.</para>
    ///
    /// <para>⚠️ <b>All three outcomes are logged, not just the successful one</b>
    /// (03-conventions 8.4.5). The stored-but-unavailable branch used to do nothing at all and
    /// leave no trace — a settings file naming a language this build no longer ships would
    /// silently fall back to English, and the log said nothing. That is precisely the shape
    /// this item exists to stop.</para>
    /// </summary>
    /// <summary>
    /// Publishes the localization service for <c>{loc:Translate}</c> to read (P1-7).
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>This is the assignment that used to happen in a constructor.</b> Doing it here
    /// makes "which instance do the views translate against" a decision of the composition root
    /// rather than a side effect of whoever resolved the service first.</para>
    ///
    /// <para>⛔ <b>Must run before any view is created.</b> <c>TranslateExtension</c> falls back
    /// to showing the raw resource key when <c>Current</c> is null -- a deliberate concession so
    /// the XAML designer still previews -- which means forgetting this step degrades the whole UI
    /// to key names <b>without throwing anything</b>. That silence is exactly why the caller
    /// asserts afterwards instead of trusting the order.</para>
    /// </remarks>
    public static void InstallLocalization(IServiceProvider services)
    {
        var localization = services.GetRequiredService<ILocalizationService>();

        if (localization is not LocalizationService concrete)
        {
            throw new InvalidOperationException(
                $"ILocalizationService resolved to {localization.GetType().Name}, but the XAML " +
                "markup extension reads the concrete LocalizationService.Current. Registering a " +
                "different implementation would leave every {loc:Translate} showing key names.");
        }

        LocalizationService.InstallAsCurrent(concrete);
    }

    public static void ApplyStoredLanguage(IServiceProvider services)
        => ApplyStoredLanguage(services, CultureInfo.CurrentUICulture);

    /// <summary>
    /// Decides the UI language at startup: <b>the stored preference if there is one, otherwise
    /// the operating system's UI language, otherwise English</b> (C-25, 2026-08-05).
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>The stored preference always wins, and that is the whole ordering</b> (user
    /// decision 2026-08-05). The system language is only ever a <i>first guess</i>, used on a
    /// machine that has never had a language chosen on it. The moment the user picks one from
    /// the menu, <c>MainWindowViewModel.SetLanguage</c> writes it to settings and this method
    /// never consults the OS again.</para>
    ///
    /// <para>⚠️ <b>The guess is deliberately NOT persisted.</b> Nothing here writes
    /// <c>IAppSettings.Language</c> — the only writer in the application is the menu command.
    /// So "the user chose this" and "we guessed this" stay distinguishable, and a user who
    /// changes their Windows language before ever touching our menu sees the app follow. ⛔
    /// Persisting the guess would freeze a decision the user never made, and would be
    /// indistinguishable afterwards from one they did.</para>
    ///
    /// <para>⚠️ <b><paramref name="systemUiCulture"/> is a parameter, not a read of
    /// <see cref="CultureInfo.CurrentUICulture"/> inside.</b> Ambient culture would make every
    /// test here depend on the machine it runs on — and this repository's development machine
    /// is Chinese, so "fresh install shows English" would pass in CI and fail locally, or the
    /// reverse. The public overload above supplies the real value.</para>
    /// </remarks>
    internal static void ApplyStoredLanguage(
        IServiceProvider services, CultureInfo systemUiCulture)
    {
        var localization = services.GetRequiredService<ILocalizationService>();
        var stored = services.GetRequiredService<IAppSettings>().Language;
        var available = localization.AvailableLanguages;
        var hasStored = !string.IsNullOrEmpty(stored);

        var match = hasStored
            ? available.FirstOrDefault(l => string.Equals(
                l.Culture.Name, stored, StringComparison.OrdinalIgnoreCase))
            : null;

        // Only consulted when nothing was ever stored -- see the ordering note above.
        var fromSystem = hasStored ? null : MatchSystemLanguage(available, systemUiCulture);

        var chosen = match ?? fromSystem;
        if (chosen is not null) localization.SetLanguage(chosen.Culture);

        var outcome = hasStored
            ? match is not null ? "stored preference applied" : "stored preference not available"
            : fromSystem is not null
                ? $"no stored preference; matched system UI language {systemUiCulture.Name}"
                : $"no stored preference; system UI language {systemUiCulture.Name} not available";

        // Read back from the service rather than from `match` -- the point of this line is
        // what is on screen, and reporting what we intended is how culture.ui got it wrong.
        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Diagnostics.Startup")
            .LogInformation(
                "UI language in effect: {Language} ({Outcome}, stored={Stored}).",
                localization.CurrentCulture.Name, outcome, stored ?? "(none)");
    }

    /// <summary>
    /// Resolves an operating-system UI culture to one of the languages this build ships,
    /// by walking the culture's parent chain. Returns null when none of them matches.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>Why the parent chain rather than an exact name match</b>: Windows reports
    /// a specific culture, not a neutral one. A Chinese machine says <c>zh-CN</c>, and this
    /// build ships <c>zh-Hans</c> — an exact comparison finds nothing and every Chinese user
    /// gets English, which is precisely the outcome this change exists to fix. The chain
    /// <c>zh-CN → zh-Hans → zh</c> matches at the second step. <c>en-US → en</c> likewise.</para>
    ///
    /// <para>⚠️ <b>Traditional Chinese falls back to English, and that is correct rather than a
    /// gap</b>: <c>zh-TW</c>'s chain is <c>zh-TW → zh-Hant → zh</c>, which never reaches
    /// <c>zh-Hans</c>. Simplified text is not what a Traditional reader asked for; English is
    /// the honest answer until a <c>zh-Hant</c> resource set exists. ⛔ Do not "fix" this by
    /// matching on the bare <c>zh</c> prefix.</para>
    ///
    /// <para><b>The loop has three terminators, and they are not redundant with each other</b>:
    /// the empty invariant name, the self-parenting check, and the depth bound. ⚠️ <b>Mutation
    /// verification found that removing the name check alone stays green</b> (2026-08-05) —
    /// invariant <i>is</i> its own parent, so the second condition catches that case too. That
    /// is an equivalent mutation, not a missing test: no input distinguishes them, and no test
    /// can. ⛔ <b>Do not "simplify" by deleting one of them</b> — the name check is what states
    /// the intent (stop at invariant, do not compare an empty name against the list), and the
    /// other two are backstops for a culture graph that misbehaves. The depth bound is the only
    /// one that is unconditionally safe, and a startup path is the worst place to discover that
    /// the other two were both wrong.</para>
    ///
    /// <para>⭐ <b>macOS (V1.1): this costs nothing to port, deliberately.</b> There is no
    /// platform-specific call here — <see cref="CultureInfo.CurrentUICulture"/> and the parent
    /// chain are BCL and ICU, and .NET derives the former from the system locale on macOS the
    /// same way it does from Windows. ⛔ <b>Do not "port" this by reaching for
    /// <c>NSUserDefaults</c>/<c>AppleLanguages</c></b>: that would put platform code in the App
    /// layer, which 02-architecture forbids, to buy nothing this does not already do.</para>
    ///
    /// <para>⚠️ <b>Two limits that apply on both platforms, worth knowing before macOS
    /// bring-up</b>:</para>
    /// <list type="number">
    ///   <item>Both systems keep an <b>ordered list</b> of preferred languages, and .NET
    ///   surfaces only the first as <c>CurrentUICulture</c>. A machine listing
    ///   <c>zh-Hant, zh-Hans, en</c> gets English, not the Simplified sitting second. Reading
    ///   the whole list needs platform code; it is not worth it for a first guess the user can
    ///   override in one click.</item>
    ///   <item>Under <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c> the culture name is empty and
    ///   this returns null — English. Handled rather than crashing, and pinned by a test.</item>
    /// </list>
    ///
    /// <para>⚠️ <b>The parent chain is CLDR data, not our logic</b>, so the cases pinned in
    /// <c>ApplyStoredLanguageTests</c> are really assertions about the ICU build in use. If
    /// macOS ever disagrees, <b>those tests go red on macOS</b> — which is the intended way to
    /// find out, and makes them a bring-up check rather than a formality.</para>
    /// </remarks>
    internal static LanguageOption? MatchSystemLanguage(
        IReadOnlyList<LanguageOption> available, CultureInfo systemUiCulture)
    {
        var culture = systemUiCulture;

        for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(culture.Name); depth++)
        {
            var hit = available.FirstOrDefault(l => string.Equals(
                l.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase));

            if (hit is not null) return hit;

            var parent = culture.Parent;
            if (ReferenceEquals(parent, culture)) break;
            culture = parent;
        }

        return null;
    }
}
