using DiSerial.Core.Abstractions;

namespace DiSerial.Core.Models;

// Hold a prepared reply rule.
public sealed record ArmedRule(long Id, string Name, byte[] Trigger, byte[] Response);

// Match automatic reply rules.
public static class AutoAnswerMatcher
{
    // Find the first matching rule.
    public static ArmedRule? FirstMatch(ReadOnlySpan<byte> frame, IReadOnlyList<ArmedRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Trigger.Length == 0) continue;
            if (frame.IndexOf(rule.Trigger) >= 0) return rule;
        }

        return null;
    }

    // Prepare enabled reply rules.
    public static IReadOnlyList<ArmedRule> Arm(
        IReadOnlyList<AutoAnswerRule> rules, Func<AutoAnswerRule, bool> isEnabled)
    {
        var armed = new List<ArmedRule>();

        foreach (var rule in rules)
        {
            if (!isEnabled(rule)) continue;

            if (!PayloadParser.TryParse(
                    rule.TriggerText, rule.TriggerFormat == PayloadFormat.Hex,
                    LineEnding.None, out var trigger))
            {
                continue;
            }

            if (!PayloadParser.TryParse(
                    rule.ResponseText, rule.ResponseFormat == PayloadFormat.Hex,
                    rule.Terminator, out var response))
            {
                continue;
            }

            if (trigger.Length == 0 || response.Length == 0) continue;

            armed.Add(new ArmedRule(rule.Id, rule.Name, trigger, response));
        }

        return armed;
    }
}
