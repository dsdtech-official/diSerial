namespace DiSerial.App.Services;

// Describe developer mode status.
public sealed record DeveloperModeState(bool IsEnabled)
{
    public static DeveloperModeState Disabled { get; } = new(false);
}
