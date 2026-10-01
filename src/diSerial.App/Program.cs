using Avalonia;
using DiSerial.App.Composition;
using Microsoft.Extensions.Logging;

namespace DiSerial.App;

// Provide the app entry point.
internal sealed class Program
{
    // Run the application.
    [STAThread]
    public static void Main(string[] args)
    {
        using var logging = LoggingBootstrap.Initialize(args);
        LoggingBootstrap.HookUnhandledExceptions(logging.LoggerFactory);

        var logger = logging.LoggerFactory.CreateLogger("Diagnostics.Lifecycle");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            logger.LogInformation("diSerial exited normally");
        }
        catch (Exception ex)
        {
            if (LoggingBootstrap.MarkLogged(ex))
            {
                logger.LogCritical(ex, "diSerial terminated by an unhandled exception");
            }

            throw;
        }
    }

    // Configure the Avalonia app.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
    }
}
