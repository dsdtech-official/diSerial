using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.Localization;

/// <summary>
/// ILocalizationService 的 RESX 实现（C-25 / C-26）。
///
/// 选用 RESX 而非 JSON 的主因：约 15 处带占位符的计算字符串
/// （Title、StatusText、注入警告文案）需要 RESX 的 comment 字段向翻译者
/// 说明每个 {0} 的含义；同时卫星程序集与区域回退由 .NET 免费提供。
///
/// 区域回退链示例：zh-Hans-CN → zh-Hans → 中性(英语)。
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new(
        "DiSerial.App.Resources.Strings", typeof(LocalizationService).Assembly);

    /// <summary>
    /// The instance the XAML markup extension reads.
    ///
    /// <para>A markup extension is constructed during XAML parsing and cannot reach the DI
    /// container, so a static entry point is needed. It is assigned <b>once, explicitly, by the
    /// composition root</b> via <see cref="InstallAsCurrent"/>; everything else takes
    /// <see cref="ILocalizationService"/> through DI. This is the project's only service-locator
    /// use, and it is a concession to the XAML framework rather than a pattern to copy.</para>
    ///
    /// <para>⛔ <b>It used to be assigned in the constructor, which was P1-7.</b> That made the
    /// value depend on <i>who resolved the service first</i> -- and, worse, meant <b>any</b>
    /// <c>new LocalizationService()</c> silently hijacked translation for the whole process.
    /// Unit tests construct this type freely, so the hijack was routine rather than exotic.</para>
    /// </summary>
    public static LocalizationService? Current { get; private set; }

    /// <summary>
    /// Publishes <paramref name="instance"/> as <see cref="Current"/>. Called by the composition
    /// root exactly once, at startup, before any view is created.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Deliberately not idempotent-by-silence.</b> If a second, different instance is ever
    /// installed, that is the ambiguity P1-7 was about and it should be loud rather than
    /// last-writer-wins. Re-installing the <i>same</i> instance is harmless and allowed, because
    /// a restart-in-place path may legitimately run the root twice.
    /// </remarks>
    public static void InstallAsCurrent(LocalizationService instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (Current is not null && !ReferenceEquals(Current, instance))
        {
            throw new InvalidOperationException(
                "A different LocalizationService is already installed as Current. The composition " +
                "root installs exactly one; a second one would silently change which resources " +
                "every {loc:Translate} in the application reads (P1-7).");
        }

        Current = instance;
    }

    /// <summary>
    /// <paramref name="logger"/> 可为 null：XAML 标记扩展与单元测试都会直接 new，
    /// 那些场景不必搭日志管线。DI 解析时会注入真实实例。
    /// </summary>
    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger ?? NullLogger<LocalizationService>.Instance;
        CurrentCulture = DefaultCulture;
    }

    private readonly ILogger _logger;

    /// <summary>默认语言为英语 —— 与 diDatatracker 主销 Amazon 美国站一致。</summary>
    public static CultureInfo DefaultCulture { get; } = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// ⭐ <b>Names are endonyms — each language written in itself</b>, so a user who switched into
    /// something unreadable can still recognise the way back. That is also why this file is on
    /// <c>SourceConventionTests.Allowed</c>.
    ///
    /// <para>⚠️ <b>Adding a language is this one line plus a <c>Strings.&lt;culture&gt;.resx</c></b>
    /// (03-conventions 2.5) — MSBuild picks the file up by convention, no csproj change. The menu
    /// is driven by this collection, so no XAML changes either.</para>
    ///
    /// <para>⛔ <b>Adding an entry here changes first-launch behaviour</b>: a fresh install matches
    /// the OS language against this list, so machines in the new language stop opening in English.
    /// That is the intent, but it means <c>ApplyStoredLanguageTests</c> has to be read as well as
    /// this line — one of its cases needs a culture this build does <i>not</i> ship.</para>
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("en", "English"),
        new("zh-Hans", "简体中文"),
        new("ja", "日本語"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("zh-Hant", "繁體中文")
    ];

    public CultureInfo CurrentCulture { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    private readonly ConcurrentDictionary<string, LocalizedString> _bindables = new();

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return Find(key) ?? $"!{key}!";
        }
    }

    public string? Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            return Resources.GetString(key, CurrentCulture);
        }
        catch (MissingManifestResourceException e)
        {
            // 资源程序集缺失（打包遗漏等）时不应连累整个界面，
            // 交由调用方走兜底路径。
            //
            // ⚠️ 记 Debug 而非 Warning：本方法**每取一个键就调一次**，
            // 资源缺失时界面上每一处文案都会触发，记 Warning 会刷屏。
            // 而且这个故障极其显眼 —— 满屏 `!Key!`，不靠日志也能发现。
            // 判据见 01-spec 4.7 共有第 1 条：会重复触发的记 Debug。
            _logger.LogDebug(e, "Resource lookup failed for {Key}; falling back.", key);
            return null;
        }
    }

    /// <summary>
    /// 按键缓存可绑定包装对象 —— 每个键全应用只有一个实例、一次事件订阅，
    /// 因此界面上重复使用同一个键（如「连接」在多处出现）不会叠加订阅。
    /// </summary>
    public LocalizedString GetBindable(string key) =>
        _bindables.GetOrAdd(key, k => new LocalizedString(this, k));

    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        if (args.Length == 0) return template;

        try
        {
            // 用 InvariantCulture 而非 CurrentCulture 做数值格式化，理由同 FrameFormatter：
            // 界面语言不应改变数字的小数点写法。
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException e)
        {
            // 译文里的占位符写错时不崩溃，直接暴露模板便于定位。
            //
            // ⚠️ 记 Debug：同一个坏键每次格式化都会抛，会重复触发。
            // 结果同样显眼 —— 界面上直接显示 `{0} · {1}` 这样的模板原文。
            _logger.LogDebug(e, "Format failed for {Key}; showing the raw template.", key);
            return template;
        }
    }

    public void SetLanguage(CultureInfo culture)
    {
        if (culture.Name == CurrentCulture.Name) return;

        CurrentCulture = culture;

        // 只设 UI 区域。CurrentCulture（数字/日期格式）刻意保持不变 ——
        // 见接口注释与 FrameFormatter 的说明。
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // 空字符串 = 「所有属性均已变更」，一次性刷新全部索引器绑定。
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
