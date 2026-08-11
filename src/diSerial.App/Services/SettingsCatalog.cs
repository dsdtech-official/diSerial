using DiSerial.Core.Models;

namespace DiSerial.App.Services;

/// <summary>Where a parameter lives across restarts.</summary>
public enum SettingsPersistence
{
    /// <summary>A row in <c>settings.db</c>, keyed by the parameter's catalog id.</summary>
    SettingsDb,

    /// <summary>
    /// Deliberately NOT persisted: every launch starts from the default.
    /// The reason is always recorded in <see cref="SettingsParameter.Notes"/>, because
    /// four of these change the bytes that actually appear on the wire.
    /// </summary>
    Never,

    /// <summary>Persisted, but owned by <c>recordings.db</c> rather than by settings.</summary>
    RecordingsDb
}

/// <summary>Which session type a parameter belongs to.</summary>
public enum SettingsScope
{
    /// <summary>One value for the whole application.</summary>
    Global,

    /// <summary>Terminal sessions only.</summary>
    Terminal,

    /// <summary>Monitor sessions only.</summary>
    Monitor,

    /// <summary>Appears in both session types (only used by non-persisted parameters).</summary>
    Both
}

/// <summary>
/// One entry in the parameter catalog.
///
/// <para><b>The catalog is the single source of truth for parameter METADATA</b> (id, type,
/// default, scope, where the user sets it). Metadata deliberately lives in code and never in
/// the database. If defaults were stored, an old database would carry the OLD default forever:
/// changing a default in a later build would silently have no effect for existing users, and
/// nothing would report it. That is the same shape as P2-77, only pointing the other way.</para>
///
/// <para>The database holds VALUES only, one row per persisted entry, keyed by
/// <see cref="Id"/>.</para>
/// </summary>
public sealed record SettingsParameter
{
    /// <summary>
    /// Catalog id, e.g. <c>P01</c>. This is the storage key and it is a permanent contract.
    ///
    /// <para>⛔ <b>Ids are never reused.</b> A parameter that is removed retires its id for
    /// good. Reusing one would make an old database's row for the retired parameter be read
    /// as the new parameter: a wrong value, silently accepted, with no way to tell.</para>
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Short English name. Used by the generated catalog table in the docs.</summary>
    public required string Name { get; init; }

    public required SettingsScope Scope { get; init; }

    public required SettingsPersistence Persistence { get; init; }

    /// <summary>Where the user changes it, or why it is not persisted.</summary>
    public required string Notes { get; init; }

    /// <summary>
    /// Reads the value out of the model as the text stored in the database.
    /// Null for entries that are not persisted in <c>settings.db</c>.
    /// </summary>
    public Func<AppSettingsModel, string>? Read { get; init; }

    /// <summary>
    /// Applies a stored text value to the model, returning the updated model, or
    /// <c>null</c> when the text cannot be understood by this build.
    ///
    /// <para>⭐ Returning null rather than throwing is what makes the failure PER ROW.
    /// The caller falls back to this one parameter's default, logs it, and keeps every other
    /// row. The whole-file fallback this replaces (P2-77) lost all of them.</para>
    /// </summary>
    public Func<AppSettingsModel, string, AppSettingsModel?>? Write { get; init; }

    /// <summary>
    /// For an entry that is <b>not</b> persisted while the model still carries the field: the
    /// leaf this entry deliberately leaves uncovered, e.g. <c>Monitor.Display.ShowSent</c>.
    ///
    /// <para>⭐ It exists so that <c>SettingsModelShapeTests</c> can tell <b>"decided not to
    /// persist"</b> from <b>"forgot to add an entry"</b> — the two look identical from the
    /// model's side, and only one of them is a defect. Writing the exception here rather than
    /// in the test keeps the catalog the single place that answers "does this field persist".</para>
    /// </summary>
    public string? UncoveredLeaf { get; init; }

    /// <summary>
    /// Short label written into the database's <c>note</c> column, e.g. <c>Baud rate (terminal)</c>.
    ///
    /// <para>⭐ Built from <see cref="Name"/> plus the scope rather than written out again:
    /// with flat numbering the same kind of parameter appears twice (P02 and P12), so the scope
    /// is the only thing that tells the two rows apart when reading the table.</para>
    ///
    /// <para>⚠️ <b>English, and that was a decision.</b> The user asked for this column using
    /// Chinese examples, and a first version wrote Chinese labels — which
    /// <c>SourceConventionTests.NoHardcodedNonAsciiUserText</c> rejected. The rule it enforces
    /// is "UI text goes through resources, developer-facing text is English", and this column is
    /// the second kind: it is never rendered in the UI, only read by whoever opens the database,
    /// and it sits beside <c>default_value</c> / <c>value_type</c> which are English tokens too.
    /// ⛔ The whitelist was deliberately NOT used: it works per FILE, so exempting this one would
    /// also disarm the check for every future string in the catalog.</para>
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Shape of the stored value: <c>int</c> / <c>bool</c> / <c>enum</c> / <c>string</c>.</summary>
    public string ValueType { get; init; } = string.Empty;
}

/// <summary>
/// Every parameter the user can set, persisted or not.
///
/// <para><b>Why the non-persisted ones are in here too</b> (user decision 2026-08-07): they are
/// the entries most likely to be "helpfully" given persistence by someone later, and four of
/// them (send format, line ending, DTR, RTS, monitor send-enable) change the bytes that go out
/// on the wire. Registering them WITH their reason makes the decision visible at the point
/// someone would undo it, instead of only in spec 4.5.</para>
///
/// <para><b>Numbering</b> (user decision 2026-08-07): flat, one id per STORED VALUE. Terminal
/// and monitor keep their own parameters and therefore their own ids, so the same kind of
/// parameter appears twice (P02 and P12 are both "baud rate"). ⚠️ The cost was raised once and
/// accepted: changing a type, range or default for one kind means editing two catalog entries,
/// and nothing checks that both were edited. The alternative considered was one id plus a scope
/// column (11 ids); see 02-architecture section 10.</para>
/// </summary>
public static class SettingsCatalog
{
    /// <summary>
    /// Highest id ever assigned, including retired ones. New parameters take
    /// <c>MaxAssignedId + 1</c>. ⛔ This only ever goes up, even when parameters are removed.
    /// </summary>
    public const int MaxAssignedId = 33;

    // ⛔⭐ THESE TWO MAPS MUST STAY ABOVE `Entries`. Static field initialisers run in
    // declaration order, and `Entries` calls Serial()/Display(), which index them. Declared
    // after `Entries` they are still null at that point, and the whole class fails to
    // initialise with a NullReferenceException wrapped in TypeInitializationException --
    // which points at whatever touched the catalog first, not at the ordering.
    //
    // ---- Human-facing labels --------------------------------------------------------
    //
    // ⚠️ Chinese by request (2026-08-07): these strings go into settings.db's `note` column,
    // whose reader is the person inspecting the database. They are DATA, not source comments,
    // so the English-comments rule does not reach them.
    //
    // ⛔ Both maps are indexed by the catalog's English Name and have NO fallback: a name that
    // is not in them throws at construction, which is a test failure. A `?? name` default would
    // silently ship an English label or an empty type column for a parameter someone added.

    private static readonly Dictionary<string, string> ValueTypes = new(StringComparer.Ordinal)
    {
        ["Baud rate"] = "int",
        ["Data bits"] = "int",
        ["Parity"] = "enum",
        ["Stop bits"] = "enum",
        ["Flow control"] = "enum",
        ["Display format"] = "enum",
        ["Timestamp mode"] = "enum",
        ["Show channel column"] = "bool",
        ["Show delta column"] = "bool",
        ["Show sent data"] = "bool"
    };

    private static string ScopeSuffix(SettingsScope scope) => scope switch
    {
        SettingsScope.Terminal => " (terminal)",
        SettingsScope.Monitor => " (monitor)",
        SettingsScope.Global => string.Empty,
        _ => " (both)"
    };

    private static readonly SettingsParameter[] Entries =
    [
        // ---- Global -----------------------------------------------------------------
        new()
        {
            Id = "P01",
            Name = "UI language",
            Scope = SettingsScope.Global,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "UI language",
            ValueType = "string",
            Notes = "Language menu. Null means never chosen, which follows the OS UI language "
                  + "(spec 4.5). Stored as the culture name, e.g. zh-Hans.",
            Read = m => m.Language ?? string.Empty,
            Write = (m, raw) => m with { Language = raw.Length == 0 ? null : raw }
        },

        new()
        {
            Id = "P33",
            Name = "Last export directory",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.SettingsDb,
            Label = "Last export directory",
            ValueType = "string",
            Notes = "Where the export dialog opens. Empty means no export has succeeded yet, in "
                  + "which case it opens in Documents. Written only after the file is actually "
                  + "written -- cancelling, or a failed write, must not move the next default "
                  + "(user decision 2026-08-10). Only the directory is kept: the file name is "
                  + "regenerated from the session and the clock every time. Read back through "
                  + "SessionViewModel.ResolveExportDirectory, which falls back to Documents and "
                  + "logs when the directory has gone (removable drive, deleted folder).",
            Read = m => m.LastExportDirectory ?? string.Empty,
            Write = (m, raw) => m with { LastExportDirectory = raw.Length == 0 ? null : raw }
        },

        // ---- Terminal: serial parameters ---------------------------------------------
        Serial("P02", "Baud rate", SettingsScope.Terminal),
        Serial("P03", "Data bits", SettingsScope.Terminal),
        Serial("P04", "Parity", SettingsScope.Terminal),
        Serial("P05", "Stop bits", SettingsScope.Terminal),
        Serial("P06", "Flow control", SettingsScope.Terminal),

        // ---- Terminal: display preferences -------------------------------------------
        Display("P07", "Display format", SettingsScope.Terminal),
        Display("P08", "Timestamp mode", SettingsScope.Terminal),
        Display("P09", "Show channel column", SettingsScope.Terminal),
        Display("P10", "Show delta column", SettingsScope.Terminal),
        Display("P11", "Show sent data", SettingsScope.Terminal),

        // ---- Monitor: serial parameters ----------------------------------------------
        Serial("P12", "Baud rate", SettingsScope.Monitor),
        Serial("P13", "Data bits", SettingsScope.Monitor),
        Serial("P14", "Parity", SettingsScope.Monitor),
        Serial("P15", "Stop bits", SettingsScope.Monitor),
        Serial("P16", "Flow control", SettingsScope.Monitor),

        // ---- Monitor: display preferences --------------------------------------------
        Display("P17", "Display format", SettingsScope.Monitor),
        Display("P18", "Timestamp mode", SettingsScope.Monitor),
        Display("P19", "Show channel column", SettingsScope.Monitor),
        Display("P20", "Show delta column", SettingsScope.Monitor),

        // ⛔ P21 RETIRED 2026-08-07 (user decision): monitor sessions do not need the
        // "show sent data" toggle. It was never reachable there anyway — the checkbox binds to
        // !IsMonitorSession — so this row could be written but never changed by the user
        // (00-STATUS P2-79). Nothing on screen changes: monitor still shows injected frames,
        // which M-09 and P1-32 require.
        //
        // ⛔ The id stays retired for good and is NOT reused. An old database still holding a
        // P21 row is simply ignored; reusing the number would make that row be read as some
        // future parameter — a wrong value, silently accepted.
        new()
        {
            Id = "P21",
            Name = "Show sent data",
            Scope = SettingsScope.Monitor,
            Persistence = SettingsPersistence.Never,
            UncoveredLeaf = "Monitor.Display.ShowSent",
            Notes = "⛔ Retired 2026-08-07. Monitor sessions have no toggle for this and do not "
                  + "need one; the value stays at its default (true), so injected frames remain "
                  + "visible as M-09 requires. The field still exists on the model because "
                  + "DisplayPreferences is shared by both session kinds."
        },

        // ---- Deliberately NOT persisted ----------------------------------------------
        // ⛔ The first five change what actually goes out on the wire. "Same as last time" is
        // an unacceptable default for those: a leftover CRLF adds two bytes to the next Modbus
        // frame and the CRC is simply wrong, without the user doing anything. Spec 4.5.
        NotPersisted("P22", "Send format (ASCII / HEX)", SettingsScope.Both,
            "Send area radio buttons. Back to ASCII every launch: it changes the bytes sent."),
        NotPersisted("P23", "Line ending", SettingsScope.Both,
            "Send area dropdown. Back to None every launch: it changes the bytes sent."),
        NotPersisted("P25", "DTR output", SettingsScope.Terminal,
            "Serial signals panel (T-07). Unticked every launch: it drives a real line."),
        NotPersisted("P26", "RTS output", SettingsScope.Terminal,
            "Serial signals panel (T-07). Unticked every launch: it drives a real line. "
          + "Disabled while hardware flow control owns RTS."),
        NotPersisted("P27", "Send enabled (M-09)", SettingsScope.Monitor,
            "Monitor sessions start with sending disabled. It is the bus injection gate; "
          + "spec 4.1 fourth constraint."),

        // The rest are not persisted for reasons that are not about wire bytes.
        NotPersisted("P24", "Timed send interval", SettingsScope.Terminal,
            "Timed send panel. Back to 1000 ms every launch."),
        NotPersisted("P28", "Port selection", SettingsScope.Both,
            "New session dialog. ⛔ Never persisted: USB serial port names are not stable, so "
          + "a remembered COM3 can silently become someone else's device."),
        NotPersisted("P29", "Channel alias", SettingsScope.Monitor,
            "Monitor session channel headers. Defaults to the port name (M-05a)."),
        NotPersisted("P30", "Scroll follow", SettingsScope.Both,
            "Transient view state, not a preference. Persisting it would start the app "
          + "not following, which reads as broken."),
        NotPersisted("P31", "Paused", SettingsScope.Both,
            "Transient view state, same reason as P30."),

        new()
        {
            Id = "P32",
            Name = "Send history",
            Scope = SettingsScope.Both,
            Persistence = SettingsPersistence.RecordingsDb,
            Notes = "Send history dropdown (T-03a). It IS persisted, but recordings.db owns it "
                  + "-- it is data the user produced, not a preference. Listed here so the "
                  + "catalog answers \"where does this live\" for everything the user can set."
        }
    ];

    /// <summary>The whole catalog, in id order.</summary>
    public static IReadOnlyList<SettingsParameter> All { get; } =
        [.. Entries.OrderBy(e => e.Id, StringComparer.Ordinal)];

    /// <summary>Only the entries that own a row in <c>settings.db</c>.</summary>
    public static IReadOnlyList<SettingsParameter> Persisted { get; } =
        [.. All.Where(e => e.Persistence == SettingsPersistence.SettingsDb)];

    // ---- Entry builders -------------------------------------------------------------
    //
    // Terminal and monitor differ only in which SessionPreferences they read and write, so the
    // accessor pair is built once per kind rather than written out twenty times. ⚠️ This does
    // NOT soften the flat-numbering cost noted on the class: the two entries still exist
    // separately and still have to be edited separately.

    private static SessionPreferences Section(AppSettingsModel m, SettingsScope scope) =>
        scope == SettingsScope.Terminal ? m.Terminal : m.Monitor;

    private static AppSettingsModel WithSection(
        AppSettingsModel m, SettingsScope scope, SessionPreferences section) =>
        scope == SettingsScope.Terminal ? m with { Terminal = section } : m with { Monitor = section };

    private static SettingsParameter Serial(string id, string name, SettingsScope scope) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.SettingsDb,
        Label = name + ScopeSuffix(scope),
        ValueType = ValueTypes[name],
        Notes = "New session dialog. Written when the dialog is confirmed, not when the "
              + "connection succeeds (spec 4.5).",
        Read = m => ReadSerial(Section(m, scope).Serial, name),
        Write = (m, raw) =>
        {
            var section = Section(m, scope);
            var updated = WriteSerial(section.Serial, name, raw);
            return updated is null ? null : WithSection(m, scope, section with { Serial = updated });
        }
    };

    private static SettingsParameter Display(string id, string name, SettingsScope scope) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.SettingsDb,
        Label = name + ScopeSuffix(scope),
        ValueType = ValueTypes[name],
        Notes = name == "Show sent data"
            ? "Send area checkbox. ⚠️ Terminal sessions only -- the checkbox binds to "
            + "!IsMonitorSession, which is why monitor's P21 was retired (00-STATUS P2-79)."
            : "Toolbar \"display settings\" flyout.",
        Read = m => ReadDisplay(Section(m, scope).Display, name),
        Write = (m, raw) =>
        {
            var section = Section(m, scope);
            var updated = WriteDisplay(section.Display, name, raw);
            return updated is null ? null : WithSection(m, scope, section with { Display = updated });
        }
    };

    private static SettingsParameter NotPersisted(
        string id, string name, SettingsScope scope, string notes) => new()
    {
        Id = id,
        Name = name,
        Scope = scope,
        Persistence = SettingsPersistence.Never,
        Notes = notes
    };

    /// <summary>What this build stores when nobody has changed the parameter.</summary>
    public static string DefaultValueOf(SettingsParameter parameter) =>
        parameter.Read!(new AppSettingsModel());

    // ---- Value accessors ------------------------------------------------------------
    //
    // Switching on the display name keeps one entry per parameter in the table above rather
    // than one lambda pair per parameter. ⛔ The names are matched with StringComparer.Ordinal
    // and a default case that throws, so a typo is a test failure, not a silently skipped row.

    private static string ReadSerial(SerialPreferences s, string name) => name switch
    {
        "Baud rate" => s.BaudRate.ToString(),
        "Data bits" => s.DataBits.ToString(),
        "Parity" => s.Parity.ToString(),
        "Stop bits" => s.StopBits.ToString(),
        "Flow control" => s.FlowControl.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown serial parameter.")
    };

    private static SerialPreferences? WriteSerial(SerialPreferences s, string name, string raw) => name switch
    {
        "Baud rate" => int.TryParse(raw, out var b) && b > 0 ? s with { BaudRate = b } : null,
        "Data bits" => int.TryParse(raw, out var d) && d is >= 5 and <= 8 ? s with { DataBits = d } : null,
        "Parity" => Enum.TryParse<SerialParity>(raw, out var p) && Enum.IsDefined(p) ? s with { Parity = p } : null,
        "Stop bits" => Enum.TryParse<SerialStopBits>(raw, out var t) && Enum.IsDefined(t) ? s with { StopBits = t } : null,
        "Flow control" => Enum.TryParse<SerialFlowControl>(raw, out var f) && Enum.IsDefined(f) ? s with { FlowControl = f } : null,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown serial parameter.")
    };

    private static string ReadDisplay(DisplayPreferences d, string name) => name switch
    {
        "Display format" => d.Format.ToString(),
        "Timestamp mode" => d.Timestamp.ToString(),
        "Show channel column" => d.ShowChannel.ToString(),
        "Show delta column" => d.ShowDelta.ToString(),
        "Show sent data" => d.ShowSent.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown display parameter.")
    };

    private static DisplayPreferences? WriteDisplay(DisplayPreferences d, string name, string raw) => name switch
    {
        "Display format" => Enum.TryParse<DisplayFormat>(raw, out var f) && Enum.IsDefined(f) ? d with { Format = f } : null,
        "Timestamp mode" => Enum.TryParse<TimestampMode>(raw, out var t) && Enum.IsDefined(t) ? d with { Timestamp = t } : null,
        "Show channel column" => bool.TryParse(raw, out var c) ? d with { ShowChannel = c } : null,
        "Show delta column" => bool.TryParse(raw, out var e) ? d with { ShowDelta = e } : null,
        "Show sent data" => bool.TryParse(raw, out var s) ? d with { ShowSent = s } : null,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown display parameter.")
    };
}
