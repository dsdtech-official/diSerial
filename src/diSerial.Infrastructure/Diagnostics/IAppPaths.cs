using System.Runtime.InteropServices;

namespace DiSerial.Infrastructure.Diagnostics;

public interface IAppPaths
{
    string ConfigDirectory { get; }

    string LogDirectory { get; }

    string RecordingDatabasePath { get; }

    string SettingsDatabasePath { get; }
}

// Resolve application data paths.
public sealed class AppPaths : IAppPaths
{
    public const string AppFolderName = "diSerial";
    public const string LogFolderName = "logs";
    public const string RecordingFileName = "recordings.db";
    public const string SettingsFileName = "settings.db";

    // Resolve application data paths.
    public AppPaths() : this(Environment.GetFolderPath)
    {
    }

    // Resolve application data paths.
    internal AppPaths(Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> getFolderPath)
    {
        ArgumentNullException.ThrowIfNull(getFolderPath);

        ConfigDirectory = Path.Combine(
            getFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            AppFolderName);

        LogDirectory = Path.Combine(ConfigDirectory, LogFolderName);
        RecordingDatabasePath = Path.Combine(ConfigDirectory, RecordingFileName);
        SettingsDatabasePath = Path.Combine(ConfigDirectory, SettingsFileName);
    }

    public string ConfigDirectory { get; }

    public string LogDirectory { get; }

    public string RecordingDatabasePath { get; }

    public string SettingsDatabasePath { get; }

    // Create default app paths.
    public static IAppPaths CreateDefault() => Default;

    private static readonly Lazy<IAppPaths> Lazy = new(() =>
    {
        _ = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        return new AppPaths();
    });

    private static IAppPaths Default => Lazy.Value;
}
