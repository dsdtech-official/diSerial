namespace DiSerial.Core.Abstractions;

/// <summary>
/// One remembered send. <b>Identity is (Text, IsHexMode), not Text alone</b> — see
/// <see cref="ISendHistoryStore"/> for why those are two different commands.
/// </summary>
/// <param name="Text">Exactly what the user typed, untouched.</param>
/// <param name="IsHexMode">
/// Which input mode produced the bytes. Restored when the entry is picked, so that picking
/// an entry reproduces the bytes it originally put on the wire.
/// </param>
/// <param name="LineEnding">
/// ⚠️ <b>Recorded, never acted on</b> — plain text on purpose.
///
/// Two reasons it is a string rather than the App's <c>LineEnding</c> enum: that enum lives in
/// the App layer and Core does not model presentation choices, and this column exists only so
/// that a later decision ("should picking an entry restore the line ending too?") can be made
/// against real data instead of a guess. Same rationale as <paramref name="UseCount"/> and the
/// weight column: store now, decide later. See 00-STATUS P2-15 for the precedent.
/// </param>
/// <param name="UseCount">How many times this exact command has been sent.</param>
/// <param name="LastUsedAt">When it was last sent. Drives both ordering and eviction.</param>
public sealed record SendHistoryEntry(
    string Text,
    bool IsHexMode,
    string? LineEnding,
    int UseCount,
    DateTimeOffset LastUsedAt);

/// <summary>
/// Send history (T-03a). <b>Two implementations, and which one a session gets is a safety
/// decision</b> — see <see cref="IVolatileSendHistoryStore"/>.
///
/// <b>⚠️ The durable implementation writes payload to disk automatically.</b> Everywhere else
/// in this project, payload only lands on disk when the user asks for it — recording is a
/// button press, and hex dumps in the log need three switches lined up (03-conventions 8.6).
/// History is the one place that persists what went onto a customer's bus without being asked,
/// which is why deleting an entry has to be reachable from the UI, and why a monitor session
/// gets the volatile implementation instead. Do not quietly widen what is stored here.
///
/// <b>Identity is (Text, IsHexMode).</b> The same characters mean different bytes in the two
/// modes — <c>01 02</c> is two bytes as HEX and five as ASCII — and picking an entry restores
/// its mode, so collapsing them onto one row would let the newer one silently rewrite what the
/// older one meant.
///
/// <b>Why the methods are synchronous.</b> The table holds at most a hundred short rows and is
/// read once per session; the work is sub-millisecond. An async signature that never awaits
/// anything real is the misleading shape 00-STATUS P2-37 records about
/// <c>ISessionRecorder.WriteAsync</c> — callers there can only <c>_ =</c> the task away.
///
/// <b>⚠️ Implementations must never let a storage failure reach the send path.</b> Losing
/// history is an inconvenience; failing to send bytes because a convenience feature broke is
/// not. Log and carry on — but log, never swallow silently (SourceConventionTests).
/// </summary>
public interface ISendHistoryStore
{
    /// <summary>
    /// Most recently used first, at most <paramref name="limit"/> rows.
    ///
    /// ⚠️ The store keeps more than it hands back — the UI shows a short list while the table
    /// retains a longer tail, so an entry that scrolls off the dropdown is still there to be
    /// promoted the next time it is used.
    /// </summary>
    IReadOnlyList<SendHistoryEntry> Load(int limit);

    /// <summary>
    /// Records one send: inserts, or bumps <c>UseCount</c> and the timestamp if this exact
    /// (text, mode) pair is already known. Trims the table back to its cap afterwards,
    /// dropping the least recently used rows.
    /// </summary>
    void Record(string text, bool isHexMode, string? lineEnding);

    /// <summary>Forgets one entry. Nothing happens if it is not there.</summary>
    void Delete(string text, bool isHexMode);

    /// <summary>
    /// How many entries are stored — <b>all of them, not the visible window</b>.
    ///
    /// ⚠️ Exists for the "clear everything" confirmation, and the distinction is the whole
    /// point of it: the dropdown shows a dozen while the table holds up to a hundred, so a
    /// prompt quoting what is on screen would understate what the user is about to destroy by
    /// roughly eight times.
    /// </summary>
    int Count();

    /// <summary>
    /// Forgets everything.
    ///
    /// ⛔ <b>Unlike every other method here, this one does NOT swallow storage failures.</b>
    /// The others degrade quietly on purpose — losing history is an inconvenience, and nothing
    /// may get between the user and sending bytes. This one is the opposite case: it exists so
    /// a user can be sure the payloads they sent to a customer's bus are gone. Reporting
    /// success while the rows are still on disk would be the tool lying about the one thing
    /// the feature is for. <b>Let it throw; the caller shows the failure.</b>
    /// </summary>
    void Clear();
}

/// <summary>
/// Send history that <b>never reaches disk</b> and dies with the process (2026-08-03, user
/// decision). This is what a <b>monitor</b> session gets.
///
/// <para>⭐ <b>Why monitor sessions are different.</b> What a monitor session sends is an
/// injection into a customer's live production bus. M-09 treats that as something to be
/// deliberate about: sending is off by default, enabling it needs a confirmation, and
/// <b>constraint 4 says the enabled state must never persist — every launch starts locked
/// again</b>. Keeping the payloads on disk contradicted that: the gate reset on every start
/// while the ammunition stayed. Terminal sessions have no such exposure, so they keep the
/// durable store and its convenience.</para>
///
/// <para>⚠️ <b>The point is that nothing is ever written, not that it is deleted later.</b>
/// "Write it and remove it on exit" was considered and rejected: a crash or a kill leaves the
/// rows behind, which is exactly the case this exists to prevent.</para>
///
/// <para>⛔ <b>Process-lifetime, not session-lifetime</b> (user decision): closing a monitor
/// session and opening another one in the same run keeps the list, so re-sending an injection
/// does not mean retyping a long hex payload — and a mistyped payload on a live bus is its own
/// hazard. Only exiting diSerial clears it.</para>
///
/// <para>⚠️ It is a separate interface purely so the App layer can ask for it <i>by name</i>:
/// the App may not reference Infrastructure outside <c>Composition/</c> (P1-47), so the type
/// that selects the implementation has to be nameable from Core. It adds no members —
/// <b>the contract really is identical; only the lifetime differs.</b></para>
/// </summary>
public interface IVolatileSendHistoryStore : ISendHistoryStore;
