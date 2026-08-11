namespace DiSerial.Core.Abstractions;

/// <summary>
/// The human-facing description of one stored parameter: what it is, what type it holds, and
/// what this build would use if the row were missing.
///
/// <para>⛔ <b>Every field here is a RENDERING OF THE CODE, never a source of truth.</b> The
/// store writes them; nothing ever decides behaviour by reading them back. They exist so that
/// somebody opening the database can understand what they are looking at without the source.</para>
///
/// <para>⛔ <b>There is no <c>default_value</c> COLUMN</b> (user decision 2026-08-07). A default
/// is handed in here on every load and written into <c>value</c> for rows the user has not
/// changed — it is never given a column of its own, because a column would eventually be read
/// as authority and would then pin an old default forever.</para>
/// </summary>
/// <param name="Note">Short human description, e.g. <c>Baud rate (terminal)</c>.</param>
/// <param name="ValueType">Shape of the value: <c>int</c> / <c>bool</c> / <c>enum</c> / <c>string</c>.</param>
/// <param name="DefaultValue">
/// What this build uses when the user has not chosen. ⭐ Passed in fresh every load, so a row
/// the user never touched follows this build's default rather than the one that happened to be
/// current when the row was first written.
/// </param>
public sealed record SettingsRowInfo(string Note, string ValueType, string DefaultValue);

/// <summary>
/// Key/value storage for user settings, one row per parameter.
///
/// <para><b>Why the seam is this narrow.</b> The store knows nothing about which parameters
/// exist, what they mean, what their types are, or what their defaults are. All of that is
/// metadata and it lives in the parameter catalog, in code. The store only moves strings.</para>
///
/// <para>⭐ <b>The reason is not tidiness.</b> The store is handed the catalog on every load and
/// writes what it is told; it never remembers a default across runs as its own. That is what
/// keeps "change a default in a later build" working: there is nothing here that could disagree
/// with the code and win.</para>
///
/// <para>⭐ <b>Per row, not per file.</b> <see cref="LoadAll"/> returns whatever rows it could
/// read. A row the caller cannot understand costs that one parameter, never the others; a
/// whole-file failure mode is what this seam exists to remove (00-STATUS P2-77).</para>
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Reads every stored row. Returns empty when there is nothing stored yet, when the store
    /// could not be opened at all, or when it was replaced because it was unreadable.
    ///
    /// <para>⛔ Never throws for missing or damaged storage: settings are a convenience, and
    /// failing to read them must not stop the application from starting. Implementations log.</para>
    /// </summary>
    IReadOnlyDictionary<string, string> LoadAll();

    /// <summary>
    /// Inserts or updates exactly the rows given, leaving every other row untouched.
    ///
    /// <para>⛔ <b>Callers must pass only the rows that actually changed.</b> Writing the full
    /// set on every change would reintroduce last-writer-wins across processes: two running
    /// instances each hold their own snapshot, so a full write by one silently discards a
    /// different parameter the other just changed. Row-scoped writes are the whole reason two
    /// instances can both persist their own edits.</para>
    ///
    /// <para>Never throws: a failed write costs the next launch's memory, not the session.</para>
    /// </summary>
    void Upsert(IReadOnlyDictionary<string, string> changedRows, IReadOnlyDictionary<string, SettingsRowInfo> info);

    /// <summary>
    /// Brings the table in line with the catalog, once, before <see cref="LoadAll"/>.
    ///
    /// <para>For every parameter given:</para>
    /// <list type="bullet">
    ///   <item><b>no row</b> → insert it with the default value and an <b>empty</b>
    ///         <c>updated_at</c>;</item>
    ///   <item><b>always</b> → refresh <c>note</c> and <c>value_type</c>;</item>
    ///   <item>⭐ <b><c>updated_at</c> empty</b> → refresh <c>value</c> to the default too.</item>
    /// </list>
    ///
    /// <para>⛔⭐ <b><c>updated_at</c> is a semantic flag, not just an audit trail</b> (user
    /// decision 2026-08-07). Empty means "nobody chose this; the value here is only a rendering
    /// of the current default". That one bit is what lets the table be complete and readable
    /// while a later build changing a default still reaches users who never touched it.
    /// ⚠️ Clearing or setting it changes behaviour. Do not treat it as decoration.</para>
    ///
    /// <para>⭐ Because this runs BEFORE the read, every value <see cref="LoadAll"/> returns is
    /// already the authoritative one — the caller needs no flag and no second code path.</para>
    ///
    /// <para>Writes nothing at all when everything already matches, which is the normal launch.</para>
    /// </summary>
    void Sync(IReadOnlyDictionary<string, SettingsRowInfo> catalog);

}
