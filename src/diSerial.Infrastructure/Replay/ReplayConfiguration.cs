using DiSerial.Infrastructure.Diagnostics;

namespace DiSerial.Infrastructure.Replay;

// Configure synthetic replay.
public static class ReplayConfiguration
{
    // Read developer replay options.
    public static ReplayOptions? From(DeveloperOptions? developer)
    {
        var options = developer ?? DeveloperOptions.Disabled;

        if (!options.DebugMode) return null;

        var raw = options.Replay;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.Ordinal) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var delivery = value.ToLowerInvariant() switch
        {
            "coalesced" => ReplayDelivery.Coalesced,
            "fragmented" => ReplayDelivery.Fragmented,
            _ => ReplayDelivery.PerFrame
        };

        var result = new ReplayOptions { Delivery = delivery };

        if (options.ReplayWindowMs > 0)
        {
            result = result with { CoalesceWindow = TimeSpan.FromMilliseconds(options.ReplayWindowMs) };
        }

        return result;
    }
}
