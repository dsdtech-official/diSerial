using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DiSerial.App.Composition;
using DiSerial.App.Localization;
using DiSerial.App.ViewModels;
using DiSerial.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiSerial.App;

// Manage application startup.
public partial class App : Application
{
    private IServiceProvider? _services;
    private MainWindowViewModel? _mainViewModel;

    private NativeMenuItem? _macOsAboutItem;

    // Initialize application state.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        CreateMacOsApplicationMenu();
    }

    // Set up the desktop application.
    public override void OnFrameworkInitializationCompleted()
    {
        _services = AppServiceCollectionExtensions.BuildAppServices();

        AppServiceCollectionExtensions.InstallLocalization(_services);

        AppServiceCollectionExtensions.ApplyStoredLanguage(_services);

        if (LocalizationService.Current is null)
        {
            throw new InvalidOperationException(
                "LocalizationService.Current is not installed. Every {loc:Translate} in the " +
                "application would render its resource key instead of text.");
        }

        DataTemplates.Add(_services.GetRequiredService<ViewLocator>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = _services.GetRequiredService<MainWindowViewModel>();

            var window = new MainWindow { DataContext = _mainViewModel };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += OnShutdownRequested;

            WireMacOsApplicationMenu(_mainViewModel);

            window.Opened += OnMainWindowOpened;

            ObserveStartup(_mainViewModel.InitializeAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Create the macOS app menu.
    private void CreateMacOsApplicationMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;

        _macOsAboutItem = new NativeMenuItem();

        var menu = new NativeMenu();
        menu.Items.Add(_macOsAboutItem);
        NativeMenu.SetMenu(this, menu);
    }

    // Bind the macOS app menu.
    private void WireMacOsApplicationMenu(MainWindowViewModel viewModel)
    {
        if (_macOsAboutItem is null) return;

        _macOsAboutItem.Command = viewModel.ShowAboutCommand;

        var header = LocalizationService.Current?.GetBindable(LocKeys.MenuHelpAbout);
        if (header is null) return;

        _macOsAboutItem.Bind(NativeMenuItem.HeaderProperty, new Binding
        {
            Source = header,
            Path = nameof(LocalizedString.Value),
            Mode = BindingMode.OneWay
        });
    }

    // Log startup task failures.
    private static void ObserveStartup(Task startup) =>
        startup.ContinueWith(
            faulted => LoggingBootstrap.Current.LoggerFactory
                .CreateLogger("Diagnostics.Crash")
                .LogError(
                    faulted.Exception,
                    "Main view model initialisation failed; hot-plug detection may not be running"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // Check window display scaling.
    private static void OnMainWindowOpened(object? sender, EventArgs e)
    {
        var logger = LoggingBootstrap.Current.LoggerFactory.CreateLogger("Diagnostics.Display");

        try
        {
            if (sender is not Window window) return;

            var screen = window.Screens?.ScreenFromWindow(window);
            if (screen is null) return;

            var message = DisplayScalingCheck.DescribeMismatch(
                window.RenderScaling, screen.Scaling, displaySizedInPoints: OperatingSystem.IsMacOS());
            if (message is not null)
            {
                logger.LogWarning("{Message}", message);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Display scaling check did not run.");
        }
    }

    // Coordinate application shutdown.
    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownHandled) return;

        _shutdownHandled = true;
        e.Cancel = true;

        try
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.DisposeAsync();
            }

            if (_services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_services is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            LoggingBootstrap.Current.LoggerFactory
                .CreateLogger("Diagnostics.Lifecycle")
                .LogError(ex, "Failure during shutdown cleanup; shutting down anyway.");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private bool _shutdownHandled;
}
