using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.Localization;

// Manage the UI language.
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new(
        "DiSerial.App.Resources.Strings", typeof(LocalizationService).Assembly);

    public static LocalizationService? Current { get; private set; }

    // Set the shared language service.
    public static void InstallAsCurrent(LocalizationService instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (Current is not null && !ReferenceEquals(Current, instance))
        {
            throw new InvalidOperationException(
                "A different LocalizationService is already installed as Current. The composition " +
                "root installs exactly one; a second one would silently change which resources " +
                "every {loc:Translate} in the application reads.");
        }

        Current = instance;
    }

    // Initialize UI language services.
    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger ?? NullLogger<LocalizationService>.Instance;
        CurrentCulture = DefaultCulture;
    }

    private readonly ILogger _logger;

    public static CultureInfo DefaultCulture { get; } = CultureInfo.GetCultureInfo("en");

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

    // Look up a translated string.
    public string? Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            return Resources.GetString(key, CurrentCulture);
        }
        catch (MissingManifestResourceException e)
        {
            _logger.LogDebug(e, "Resource lookup failed for {Key}; falling back.", key);
            return null;
        }
    }

    // Get a live localized string.
    public LocalizedString GetBindable(string key) =>
        _bindables.GetOrAdd(key, k => new LocalizedString(this, k));

    // Format text with arguments.
    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        if (args.Length == 0) return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException e)
        {
            _logger.LogDebug(e, "Format failed for {Key}; showing the raw template.", key);
            return template;
        }
    }

    // Change the active UI language.
    public void SetLanguage(CultureInfo culture)
    {
        if (culture.Name == CurrentCulture.Name) return;

        CurrentCulture = culture;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
