namespace DiSerial.App.Controls;

// Evaluate scroll follow state.
internal static class ScrollFollow
{
    private const double BottomTolerance = 4.0;

    // Compute maximum scroll offset.
    public static double MaxOffset(double extentHeight, double viewportHeight)
        => Math.Max(0.0, extentHeight - viewportHeight);

    // Check proximity to the bottom.
    public static bool IsAtBottom(double offsetY, double extentHeight, double viewportHeight)
        => offsetY >= MaxOffset(extentHeight, viewportHeight) - BottomTolerance;

    // Check whether to follow content.
    public static bool ShouldFollow(
        double offsetY,
        double extentHeightBefore,
        double viewportHeightBefore,
        double extentHeightAfter,
        double viewportHeightAfter)
        => IsAtBottom(offsetY, extentHeightBefore, viewportHeightBefore)
           || IsAtBottom(offsetY, extentHeightAfter, viewportHeightAfter);
}
