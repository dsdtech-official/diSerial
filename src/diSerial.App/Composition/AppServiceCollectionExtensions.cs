using System.Globalization;
using DiSerial.Infrastructure.Localization;
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

// Register application services.
public static class AppServiceCollectionExtensions
{
    // Build application services.
    public static IServiceProvider BuildAppServices()
    {
        return CreateAppServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    // Create application services.
    public static IServiceCollection CreateAppServices()
    {
        var services = new ServiceCollection();

        var logging = LoggingBootstrap.Current;
        var developer = logging.Developer;

        services.AddDiSerialInfrastructure(
            logging.Clock, logging.LoggerFactory, logging.Options, developer,
            sp => sp.GetRequiredService<IAppSettings>().Options.IdleGapOverride);

        services.AddSingleton(new DeveloperModeState(developer.DebugMode));

        services.AddSingleton(logging.SessionLog);


        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IEnumChoiceProvider, EnumChoiceProvider>();
        services.AddSingleton<IAppSettings>(sp => new StoredAppSettings(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetService<ILogger<StoredAppSettings>>()));

        services.AddSingleton<ILocalizedTextResolver, LocalizedTextResolver>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISessionViewModelFactory, SessionViewModelFactory>();

        services.AddSingleton<IPeriodicTimerFactory, DispatcherPeriodicTimerFactory>();

        services.AddSingleton<ISendHistoryScheduler, BackgroundSendHistoryScheduler>();

        services.AddSingleton<SessionContext>();


        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<ISessionTypeCatalog, SessionTypeCatalog>();

        services.AddSingleton<Func<NewSessionDialogViewModel>>(sp =>
            () => ActivatorUtilities.CreateInstance<NewSessionDialogViewModel>(sp));

        services.AddSingleton<Func<long?, RecordingManagerViewModel>>(sp =>
            liveBatchId => new RecordingManagerViewModel(
                sp.GetRequiredService<IRecordingCatalog>(),
                sp.GetRequiredService<IRecordingReader>(),
                sp.GetRequiredService<IExportService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IEnumChoiceProvider>(),
                sp.GetRequiredService<IAppSettings>(),
                sp.GetRequiredService<ILocalizationService>(),
                liveBatchId,
                sp.GetService<ILoggerFactory>()?.CreateLogger("Recording")));

        services.AddSingleton<Func<AppOptions, OptionsDialogViewModel>>(sp =>
            current => new OptionsDialogViewModel(
                sp.GetRequiredService<ILocalizationService>(),
                current));

        services.AddSingleton<ViewLocator>();

        return services;
    }

    // Install localization.
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

    // Apply the saved UI language.
    public static void ApplyStoredLanguage(IServiceProvider services)
        => ApplyStoredLanguage(
            services,
            SystemUiLocale.Create().GetPreferred() ?? CultureInfo.CurrentUICulture);

    // Apply the saved UI language.
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

        var fromSystem = hasStored ? null : MatchSystemLanguage(available, systemUiCulture);

        var chosen = match ?? fromSystem;
        if (chosen is not null) localization.SetLanguage(chosen.Culture);

        var outcome = hasStored
            ? match is not null ? "stored preference applied" : "stored preference not available"
            : fromSystem is not null
                ? $"no stored preference; matched system UI language {systemUiCulture.Name}"
                : $"no stored preference; system UI language {systemUiCulture.Name} not available";

        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Diagnostics.Startup")
            .LogInformation(
                "UI language in effect: {Language} ({Outcome}, stored={Stored}).",
                localization.CurrentCulture.Name, outcome, stored ?? "(none)");
    }

    // Match the system UI language.
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
