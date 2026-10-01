namespace DiSerial.App.Composition;

// Check display scaling.
internal static class DisplayScalingCheck
{
    private const double Tolerance = 0.01;

    // Describe a scaling mismatch.
    public static string? DescribeMismatch(
        double renderScaling,
        double screenScaling,
        bool displaySizedInPoints)
    {
        if (displaySizedInPoints)
        {
            return null;
        }

        if (screenScaling <= 0 || renderScaling <= 0)
        {
            return null;
        }

        if (Math.Abs(renderScaling - screenScaling) <= Tolerance)
        {
            return null;
        }

        var percent = (int)Math.Round(renderScaling / screenScaling * 100.0);

        return $"Window render scaling {renderScaling} does not match its monitor scaling " +
               $"{screenScaling}: the UI is being drawn at about {percent}% of the size the " +
               $"display asks for. Both numbers come from Avalonia itself, so this is the " +
               $"framework disagreeing with itself, not a display setting. Known cause: a " +
               $"win-x86 build running under ARM64 emulation.";
    }
}
