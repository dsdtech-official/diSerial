namespace DiSerial.App.Composition;

/// <summary>
/// Reports the one defect a released build can have that nothing else in this project
/// can see: the window being drawn at a different scale than its monitor asks for.
///
/// <b>Why this exists (P2-75, 2026-08-06)</b>: the <c>win-x86</c> package renders at 100%
/// on a scaled display when it runs under ARM64 emulation. The window comes out ~20-33%
/// too small and every glyph with it. Nothing reported it -- 757 unit tests were green,
/// the package had been published twice, three real-hardware rounds had been walked. It was
/// found by a user putting two windows side by side in a screenshot.
///
/// <b>The fingerprint is exact and comes entirely from Avalonia's own public API</b>: inside
/// the affected process <c>Window.RenderScaling</c> reads 1.0 while
/// <c>Screens.ScreenFromWindow(window).Scaling</c> correctly reads the monitor's real scale.
/// Avalonia disagrees with itself. No P/Invoke and no system DPI call is needed to spot it,
/// and the check is blind to the cause -- a different future reason for the same mismatch
/// still trips it.
///
/// <b>⛔ This does not fix anything.</b> The window is still small; the point is that the
/// log now says so, on the user's machine, in the released build.
///
/// <b>⚠️ Known blind spot, stated on purpose</b>: today the check works because Avalonia is
/// wrong on ONE side only. If a future break makes <c>Screens</c> report 1.0 as well, the two
/// values agree again and this goes quiet. It detects disagreement, not wrongness.
/// </summary>
internal static class DisplayScalingCheck
{
    /// <summary>
    /// Scalings are doubles that came from a DPI ratio (120/96 = 1.25), so they compare
    /// exactly in practice -- but a tolerance costs nothing and keeps a future
    /// 1.7999999999999998 from producing a warning nobody can act on. It is far below any
    /// real Windows scale step (the smallest is 25%).
    /// </summary>
    private const double Tolerance = 0.01;

    /// <summary>
    /// Returns the warning text, or <c>null</c> when the two scalings agree.
    ///
    /// Kept as a pure function taking two doubles so the guardrail can drive it: a unit test
    /// cannot conjure an x86-on-ARM64 process, so what IS testable is that the comparison
    /// reports when it should. See <c>DisplayScalingCheckTests</c>, and 03-conventions on
    /// covering "does the scanner REPORT" rather than only "does the criterion recognise".
    /// </summary>
    public static string? DescribeMismatch(double renderScaling, double screenScaling)
    {
        if (screenScaling <= 0 || renderScaling <= 0)
        {
            // Not a mismatch, just nothing to compare. Saying anything here would be noise.
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
               $"win-x86 build running under ARM64 emulation (00-STATUS P2-75).";
    }
}
