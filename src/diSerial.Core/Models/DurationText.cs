using System.Globalization;

namespace DiSerial.Core.Models;

// Format millisecond durations.
public static class DurationText
{
    // Format a millisecond duration.
    public static string Milliseconds(TimeSpan delta) =>
        delta.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);
}
