using System.Globalization;

namespace DiSerial.Core.Models;

/// <summary>
/// The canonical rendering of a millisecond duration.
///
/// <para><b>Why this is one function in Core and not a member of
/// <c>IFrameFormatter</c></b> (P1-47, user decision 2026-08-02): it was a
/// <c>public static</c> on <c>FrameFormatter</c>, the Infrastructure implementation, and the
/// display layer reached it by fully-qualified name — the one real breach of "App references
/// Infrastructure only from Composition/". Both ways out were on the table; putting it on the
/// interface was rejected for the reason that removed <c>ISerialPort.ApplySettingsAsync</c>
/// (P2-43): <b>an interface member is an obligation</b>, and every future implementation —
/// the macOS one included — would have to carry a formatting method it has no opinion about.</para>
///
/// <para>⚠️ <b>The cost, recorded rather than argued away</b> (03-conventions 0.4): this is
/// presentation wording living in the domain layer, which is the very thing
/// <c>00-STATUS P2-34</c> keeps a list of. What makes it the lesser evil is that it states no
/// UI intent — compare <c>SerialPortSettings.ShortDescription</c>, whose own comment says it
/// is "for the status bar and the title". This one is a value-to-string rule that the log,
/// the export and the display are all required to agree on.</para>
///
/// <para>⚠️ <b><see cref="CultureInfo.InvariantCulture"/> is not a detail.</b> Following the
/// UI language would print "4,1" in German and split a TSV column in half. Engineering tools
/// keep the data format independent of the menu language — see 03-conventions, pitfall 2.</para>
/// </summary>
public static class DurationText
{
    /// <summary>
    /// Milliseconds to one decimal place, invariant: <c>4.1</c>, <c>96.0</c>, <c>-3.9</c>.
    ///
    /// <para>⚠️ Negative values are rendered as-is on purpose — a negative cross-channel Δ is
    /// the observable signal that arrival order and timestamp order disagree, and spec 4.9.2
    /// requires it to be shown, not blanked, clamped or made absolute.</para>
    /// </summary>
    public static string Milliseconds(TimeSpan delta) =>
        delta.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);
}
