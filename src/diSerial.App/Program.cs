using Avalonia;
using DiSerial.App.Composition;
using Microsoft.Extensions.Logging;

namespace DiSerial.App;

internal sealed class Program
{
    // Initialization code. Do not use any Avalonia, third-party APIs or any code that
    // depends on a SynchronizationContext before AppMain is called: the framework is not
    // initialized yet at that point.
    //
    // ⚠️ Logging is the one exception -- it has to come up first. Without ICU present the
    // app dies while the DI container resolves LocalizationService, i.e. before any window
    // exists; if logging were not running by then, the most valuable crash of all would
    // leave no record whatsoever.
    [STAThread]
    public static void Main(string[] args)
    {
        using var logging = LoggingBootstrap.Initialize();
        LoggingBootstrap.HookUnhandledExceptions(logging.LoggerFactory);

        var logger = logging.LoggerFactory.CreateLogger("Diagnostics.Lifecycle");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            logger.LogInformation("diSerial exited normally");
        }
        catch (Exception ex)
        {
            // A throw out of StartWithClassicDesktopLifetime means startup failed or the main
            // loop died. Log here and rethrow -- the AppDomain-level handler is not guaranteed
            // to run on every termination path. MarkLogged de-duplicates against that handler
            // so the same crash stack is not recorded twice.
            if (LoggingBootstrap.MarkLogged(ex))
            {
                logger.LogCritical(ex, "diSerial terminated by an unhandled exception");
            }

            throw;
        }
    }

    // Avalonia configuration. The visual designer calls this too -- do not remove it.
    //
    // There is deliberately no DevTools hook here. AvaloniaUI.DiagnosticsSupport was
    // removed on 2026-08-09: the package declares no license at all, and it is the
    // companion to Avalonia's commercial Accelerate tooling, so that omission is very
    // likely intentional rather than an upstream oversight. Re-adding it would put an
    // unlicensed dependency back into the published package.
    //
    // The MIT-licensed Avalonia.Diagnostics is NOT a drop-in replacement: it stops at
    // 11.3.19, and on Avalonia 12.1 it compiles and AttachDevTools() returns without
    // throwing -- then dies with a TypeLoadException the moment F12 is actually pressed
    // (Avalonia.Controls.Chrome.TitleBar is gone in 12.x). Measured 2026-08-09.
    //
    // Full reasoning, and what was measured before deciding: docs/02-architecture.md 11.2.3.
    public static AppBuilder BuildAvaloniaApp()
    {
        // ⚠️ .LogToTrace() is deliberately NOT used: it writes to System.Diagnostics.Trace
        // listeners, and a GUI process with no debugger attached has none -- so Avalonia's
        // binding errors were simply dropped. LoggingBootstrap now wires
        // Avalonia.Logging.Logger.Sink into this project's logging pipeline instead (see
        // AvaloniaLogBridge). This is independent of debugMode: both forms are wired.
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
    }
}
