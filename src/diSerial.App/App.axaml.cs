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

public partial class App : Application
{
    private IServiceProvider? _services;
    private MainWindowViewModel? _mainViewModel;

    /// <summary>
    /// The macOS "About" item, or null off macOS (P2-112). Held because it is created in
    /// <see cref="Initialize"/> and can only be filled in once the view model exists.
    /// </summary>
    private NativeMenuItem? _macOsAboutItem;

    /// <summary>
    /// ⛔ <b>The native menu has to be attached HERE, and that is measured, not stylistic</b>
    /// (P2-112, 2026-08-14). macOS builds the application menu once during platform startup and
    /// reads <c>NativeMenu.GetMenu(Application.Current)</c> at that moment. Calling
    /// <c>SetMenu</c> from <see cref="OnFrameworkInitializationCompleted"/> instead is
    /// <b>silently ignored</b> -- no exception, no log line, the first item just stays
    /// "About Avalonia". The same code was run from both places to establish that.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        CreateMacOsApplicationMenu();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = AppServiceCollectionExtensions.BuildAppServices();

        // {loc:Translate} reads LocalizationService.Current, which the composition root
        // installs explicitly (P1-7).
        // ⛔ This must happen before any view is created: forgetting it throws nothing, it
        // just degrades the entire UI into resource key names.
        AppServiceCollectionExtensions.InstallLocalization(_services);

        // The language has to be applied before any view model is created, otherwise the
        // first frame renders in English and then jumps.
        AppServiceCollectionExtensions.ApplyStoredLanguage(_services);

        // ⭐ Turn "forgot to install it" into a startup failure rather than a screen full of
        // keys like Menu.File (P1-7).
        // The assertion lives here rather than inside InstallLocalization because what it
        // guards is that the precondition holds for the line below: it only catches anything
        // if someone deletes the call above -- written inside the call itself, it could not.
        if (LocalizationService.Current is null)
        {
            throw new InvalidOperationException(
                "LocalizationService.Current is not installed. Every {loc:Translate} in the " +
                "application would render its resource key instead of text (P1-7).");
        }

        // ViewLocator depends on the container, so it is registered as a global DataTemplate
        // from code here rather than in markup.
        DataTemplates.Add(_services.GetRequiredService<ViewLocator>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = _services.GetRequiredService<MainWindowViewModel>();

            var window = new MainWindow { DataContext = _mainViewModel };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += OnShutdownRequested;

            WireMacOsApplicationMenu(_mainViewModel);

            // P2-75. Has to run on Opened, not here: before the window is shown it has no
            // HWND, so neither RenderScaling nor ScreenFromWindow means anything yet.
            window.Opened += OnMainWindowOpened;

            ObserveStartup(_mainViewModel.InitializeAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// P2-112, first half: claim the macOS application menu so its first item stops saying
    /// "About Avalonia".
    ///
    /// <para><b>Why a native menu is needed at all.</b> <c>Application.Name</c> (App.axaml)
    /// already fixes the menu bar title and the "Hide ..." item. Measured 2026-08-14 on a
    /// MacBook Air M4: <b>the About item does not follow it</b> and stays "About Avalonia".
    /// That is why this fix is not one attribute.</para>
    ///
    /// <para>⭐ <b>Avalonia merges rather than replaces</b>, also measured: a menu holding one
    /// item yields "About diSerial, Services, Hide diSerial, Hide Others, Show All, Quit".
    /// ⛔ So do not add Quit or Hide here. macOS supplies them, and a duplicate would be the
    /// very defect this is fixing, one item along.</para>
    ///
    /// <para>⛔ <b>The item is created empty on purpose.</b> This runs during
    /// <see cref="Initialize"/>, where neither the view model nor
    /// <c>LocalizationService.Current</c> exists yet, and the attach cannot be postponed (see
    /// <see cref="Initialize"/>). Measured: setting <c>Header</c> and <c>Command</c> afterwards
    /// does reach the live menu, so the two halves can be split.
    /// <see cref="WireMacOsApplicationMenu"/> is the other half.</para>
    ///
    /// <para>⛔ <b>Gated on macOS.</b> An application-level <c>NativeMenu</c> IS the macOS
    /// application menu; Windows has no such position.</para>
    /// </summary>
    private void CreateMacOsApplicationMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;

        _macOsAboutItem = new NativeMenuItem();

        var menu = new NativeMenu();
        menu.Items.Add(_macOsAboutItem);
        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>
    /// P2-112, second half: point the About item at the product's own About dialog and give it
    /// a header that follows the current language.
    ///
    /// <para>⭐ <b>The header is bound, not assigned.</b> The language switches at runtime
    /// (P1-7); an assigned string would leave the system menu bar frozen in whatever language
    /// the app started in while every other menu followed along.</para>
    ///
    /// <para>⚠️ Does nothing off macOS, because <see cref="CreateMacOsApplicationMenu"/> left
    /// the field null there.</para>
    /// </summary>
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

    /// <summary>
    /// P2-80: observe the startup task instead of discarding it.
    ///
    /// <para>⛔ <b>This used to be <c>_ = _mainViewModel.InitializeAsync();</c></b>, which drops
    /// the task and the exception with it. There is a process-level
    /// <c>TaskScheduler.UnobservedTaskException</c> hook, but it only runs when the task is
    /// garbage collected: the timing is not defined, and if the process exits first the line
    /// is never written at all. A user reporting "the port dropdown stopped refreshing" would
    /// hand over a log with nothing in it.</para>
    ///
    /// <para>⭐ <b>Deliberately not awaited.</b> This runs during framework initialisation --
    /// blocking here would delay the window appearing for as long as the enumeration takes
    /// (a WMI query, ~250ms on Windows). What was wrong was never the fire-and-forget, it was
    /// forgetting to look at the result.</para>
    ///
    /// <para>⚠️ <b>Nothing is rethrown.</b> Startup carries on: the watcher itself now survives
    /// a failed baseline (P2-80), so anything reaching here is already degraded rather than
    /// fatal, and taking the window down over it would be the worse trade.</para>
    ///
    /// <para>⛔ <b>The logger comes from <see cref="LoggingBootstrap.Current"/>, never from the
    /// container</b> (P2-92, 2026-08-08). This continuation used to resolve
    /// <c>ILoggerFactory</c> out of <c>IServiceProvider</c>, and that had a hole in exactly the
    /// case it exists for: the container is disposed during shutdown, so a startup task that
    /// faults while the user is closing the window made <c>GetRequiredService</c> throw, and the
    /// throw landed on this very continuation -- an unobserved faulted task. ⚠️ <b>Both layers
    /// silent</b>: the failure nobody noticed, and the recording of it failing too.</para>
    ///
    /// <para>⭐ <b>Measured, not reasoned</b> (2026-08-08, probe on a real start/stop): after
    /// the container's <c>DisposeAsync</c>, resolving from it throws
    /// <c>ObjectDisposedException</c>, while the static factory still writes and the line
    /// reaches the log file. The static pipeline is torn down by <c>using var logging</c> in
    /// <c>Program.Main</c>, which happens after the message loop returns -- later than anything
    /// here. <c>OnShutdownRequested</c>'s own catch block already relied on this; the two are
    /// now consistent.</para>
    /// </summary>
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

    /// <summary>
    /// P2-75: report a window whose render scaling disagrees with its own monitor's.
    ///
    /// <b>Log only, no UI</b> (decision 2026-08-06): the user cannot act on it from inside
    /// the app, and a banner would sit there on every start until the framework is fixed.
    /// What this buys is that a released build finally leaves evidence -- the defect it
    /// watches for was found by a human comparing two screenshots.
    ///
    /// ⚠️ <b>Never let diagnostics take the app down</b>: this runs on the UI thread during
    /// window activation, so anything thrown here would surface as a startup crash. The one
    /// thing worse than an undersized window is not getting a window at all.
    /// </summary>
    private static void OnMainWindowOpened(object? sender, EventArgs e)
    {
        var logger = LoggingBootstrap.Current.LoggerFactory.CreateLogger("Diagnostics.Display");

        try
        {
            if (sender is not Window window) return;

            var screen = window.Screens?.ScreenFromWindow(window);
            if (screen is null) return;

            // ⛔ P2-111: macOS sizes displays in points, so RenderScaling 2 against
            // Screen.Scaling 1 is correct there, not a defect. Passing the platform in keeps
            // the suppression testable from Windows; see DisplayScalingCheck.
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

    /// <summary>
    /// Shutdown runs in two passes -- cancel, clean up asynchronously, then shut down for
    /// real. <b>Nothing here may wait synchronously on async work.</b>
    ///
    /// <para>⚠️ This used to be <c>DisposeAsync().AsTask().GetAwaiter().GetResult()</c>, and
    /// the result was that <b>the process never exited after the window closed</b> (measured:
    /// still alive 25 seconds later, with no window on screen).</para>
    ///
    /// <para>The cause is a textbook sync-over-async deadlock: this method runs on the UI
    /// thread, and every <c>await</c> along the disposal chain posts its continuation back to
    /// the UI thread (this project does not use <c>ConfigureAwait(false)</c>) -- while the UI
    /// thread is blocked in <c>GetResult()</c> waiting for those very continuations.</para>
    ///
    /// <para>The tell was unambiguous: the "exited normally" line in <c>Program.Main</c> never
    /// appeared, which means the message loop had not returned at all -- so this was never a
    /// stray foreground thread lingering during shutdown.</para>
    /// </summary>
    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Second pass (the Shutdown we call ourselves once cleanup is done): let it through.
        if (_shutdownHandled) return;

        _shutdownHandled = true;
        e.Cancel = true;

        try
        {
            if (_mainViewModel is not null)
            {
                // Dispose the capture session and the device watcher so no background
                // thread is left behind.
                await _mainViewModel.DisposeAsync();
            }

            // ⚠️ DisposeAsync is mandatory here: the container holds a service that only
            // implements IAsyncDisposable (PollingDeviceWatcher), and a synchronous Dispose
            // throws "type only implements IAsyncDisposable. Use DisposeAsync to dispose the
            // container."
            //
            // That defect sat hidden behind the deadlock above -- execution never got this far.
            // The consequence is not just one exception: once the container throws, the
            // singletons after it are never disposed, and that includes StoredAppSettings,
            // which is what flushes settings to disk (it was called JsonAppSettings before
            // 2026-08-07 and was renamed when the storage changed in P2-77; the comment did
            // not follow, see P2-82).
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
            // A failed cleanup must not block the exit -- otherwise the only way out is Task
            // Manager. An exception from an async void method has no caller to catch it, so
            // it has to land here.
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
