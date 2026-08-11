using DiSerial.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.Services;

/// <summary>
/// 用户设置的唯一入口。**赋值即持久化** —— 调用点不需要决定何时存盘。
///
/// <code>
/// settings.Language = "zh-Hans";
/// settings.Terminal = settings.Terminal with { Serial = SerialPreferences.From(s) };
/// </code>
///
/// <b>为什么不沿用 Load() / Save() 两段式</b>：设置项会越加越多，
/// 两段式意味着每个新调用点都要重新回答「我该在哪一步存盘」。
/// 单一所有者 + 赋值即持久化，让新增设置项自动获得持久化，
/// 而写盘去抖与差量写入只实现一次。
/// </summary>
public interface IAppSettings
{
    /// <summary>界面语言（如 <c>zh-Hans</c>）。null 表示从未选择过。</summary>
    string? Language { get; set; }

    /// <summary>
    /// 上一次导出成功到的目录（P33）。null 表示还没成功导出过 —— 那时导出对话框开在「文档」。
    /// ⚠️ <b>取用前必须确认它还存在</b>，见 <c>SessionViewModel.ResolveExportDirectory</c>。
    /// </summary>
    string? LastExportDirectory { get; set; }

    SessionPreferences Terminal { get; set; }

    SessionPreferences Monitor { get; set; }

    // MonitorSyncParameters (M-03) was removed from this interface on 2026-08-02 (P1-49).
    // Full rationale next to AppSettingsModel.Monitor.

    /// <summary>立即把待写内容落盘。进程退出前调用，避免丢掉去抖窗口内的改动。</summary>
    void Flush();
}

/// <summary>
/// Settings backed by <see cref="ISettingsStore"/>, one row per catalog parameter
/// (2026-08-07, replaces the single-JSON-file implementation).
///
/// <para><b>What the change bought</b> (00-STATUS P2-77). The JSON version deserialised the
/// whole file in one call, so one value this build could not understand threw, and the
/// <c>catch</c> returned a fresh default model: <b>all 21 stored values were lost because of
/// one</b>. The user's symptom was the interface language reverting after an unrelated change
/// to the timestamp mode. Here each row is applied on its own, so an unreadable row costs that
/// one parameter and logs.</para>
///
/// <para>⚠️ <b>Per-row writes are load-bearing, not an optimisation.</b> Nothing stops two
/// instances of the app running. Each holds its own snapshot, so writing every row on every
/// change would mean the second one to save silently discards whatever the first one changed.
/// Writing only what actually changed is what lets both survive.</para>
/// </summary>
public sealed class StoredAppSettings : IAppSettings, IDisposable
{
    /// <summary>
    /// 写盘去抖窗口。用户连点几个开关时合并成一次写入。
    ///
    /// 取值偏短是有意的：本应用目前存在「关窗后进程不退出」的缺陷（P1-12），
    /// 极端情况下 <see cref="Flush"/> 可能等不到执行 —— 窗口短一些，
    /// 实际改动能更快落盘。
    /// </summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsStore _store;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    /// <summary>
    /// Serialises <see cref="Persist"/> against itself (2026-08-08).
    ///
    /// <para><b>Two callers exist and they can overlap.</b> The 300 ms debounce timer fires on a
    /// thread-pool thread; <see cref="Flush"/> runs on whichever thread disposes this object at
    /// shutdown. ⚠️ <c>Timer.Change(Infinite)</c> at the top of <see cref="Flush"/> stops
    /// <i>future</i> firings — <b>it does not wait for one already running</b>, so closing the
    /// window ~300 ms after changing a preference can put two threads inside
    /// <see cref="Persist"/> at once.</para>
    ///
    /// <para>⛔ <b>What was actually exposed</b>: <see cref="_persisted"/> is a plain
    /// <see cref="Dictionary{TKey,TValue}"/>, read and then written near the end of
    /// <see cref="Persist"/> <b>outside any lock</b>. Concurrent write plus read on one of those
    /// is undefined — in the worst case a resize loops forever, which at shutdown would hang the
    /// process rather than lose a setting.</para>
    ///
    /// <para>⭐ <b>Deliberately not <see cref="_gate"/></b>: that one guards
    /// <see cref="_model"/>, and every property getter takes it. Holding it across the SQLite
    /// write would block those getters on the UI thread for the duration of a disk write, which
    /// is a much worse trade than the race it would close.</para>
    ///
    /// <para>⚠️ <b>The store itself was already safe</b> — <c>SqliteSettingsStore</c> takes its
    /// own lock in <c>LoadAll</c> / <c>Sync</c> / <c>Upsert</c> and opens a fresh connection per
    /// call. This lock exists for the dictionary above it, not for the database.</para>
    /// </summary>
    private readonly Lock _persistGate = new();

    /// <summary>
    /// What the store is believed to hold, keyed by catalog id. <see cref="Persist"/> diffs the
    /// current model against this to find the rows that actually changed.
    ///
    /// <para>⚠️ It starts as what was <b>read</b>, not as the defaults. Otherwise the first save
    /// would rewrite every row whose value merely equals its default, which is exactly the
    /// full-set write the per-row design exists to avoid.</para>
    /// </summary>
    private readonly Dictionary<string, string> _persisted = new(StringComparer.Ordinal);

    /// <summary>
    /// What each parameter reads as on a model nobody has touched.
    ///
    /// <para>⭐ A row that is absent from the store means "this build's default", so that is
    /// what an absent row must be compared against. Comparing against "nothing" instead would
    /// make the first save write all 21 rows — and a full write is exactly what breaks two
    /// instances coexisting, which is the property per-row writes exist to provide.</para>
    ///
    /// <para>⚠️ <b>The accepted consequence</b>: choosing a value that happens to equal the
    /// current default stores nothing, so a later build that changes that default changes what
    /// the user sees. That is the right side to err on — they never expressed a preference
    /// different from the default — but it is a real trade and not an oversight.</para>
    /// </summary>
    private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);

    private AppSettingsModel _model;
    private bool _disposed;

    public StoredAppSettings(ISettingsStore store, ILogger<StoredAppSettings>? logger = null)
    {
        _store = store;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _model = Load();

        // 回调再包一层兜底：Persist 内部已捕获所有异常，但万一将来有人在那之外
        // 加了代码，线程池回调上的未捕获异常会**直接终止进程**。
        // 为了存一个设置项而丢掉用户正在抓的数据，完全不成比例。
        _timer = new Timer(_ => SafePersist(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public string? Language
    {
        get { lock (_gate) return _model.Language; }
        set => Mutate(m => m with { Language = value });
    }

    public string? LastExportDirectory
    {
        get { lock (_gate) return _model.LastExportDirectory; }
        set => Mutate(m => m with { LastExportDirectory = value });
    }

    public SessionPreferences Terminal
    {
        get { lock (_gate) return _model.Terminal; }
        set => Mutate(m => m with { Terminal = value });
    }

    public SessionPreferences Monitor
    {
        get { lock (_gate) return _model.Monitor; }
        set => Mutate(m => m with { Monitor = value });
    }

    public void Flush()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Persist();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Flush();
        _timer.Dispose();
    }

    private void Mutate(Func<AppSettingsModel, AppSettingsModel> change)
    {
        lock (_gate) _model = change(_model);

        // 记下「有改动排队了」。没有这一条就分不清
        // 「根本没触发」与「触发了但写失败」—— 这两种成因的排查方向完全不同。
        _logger.LogDebug("Settings change queued; write scheduled in {Delay} ms.", SaveDelay.TotalMilliseconds);

        // 去抖：每次改动重置计时器，静默 300ms 后写一次。
        _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The human-facing description of every persisted parameter, taken from the catalog.
    ///
    /// <para>⛔ Built once from the catalog and pushed to the store on every load; nothing is
    /// ever read back from storage into it. See <see cref="SettingsRowInfo"/>: the direction
    /// code -> database is what keeps a later build's changed default able to reach a user who
    /// never touched that parameter.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SettingsRowInfo> Descriptions =
        SettingsCatalog.Persisted.ToDictionary(
            p => p.Id,
            p => new SettingsRowInfo(p.Label, p.ValueType, SettingsCatalog.DefaultValueOf(p)),
            StringComparer.Ordinal);

    /// <summary>线程池回调的兜底包装。见构造函数中的说明。</summary>
    private void SafePersist()
    {
        try
        {
            Persist();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected failure while persisting settings.");
        }
    }

    /// <summary>
    /// Builds the model from the defaults, then applies the stored rows one at a time.
    ///
    /// <para>⭐ Starting from <c>new AppSettingsModel()</c> and overlaying rows is what makes a
    /// missing row mean "use this build's default" rather than "zero". A parameter added in a
    /// later build therefore needs no migration: its row simply is not there yet.</para>
    /// </summary>
    private AppSettingsModel Load()
    {
        var model = new AppSettingsModel();

        // ⛔ Sync BEFORE reading, and the order is the point. Sync seeds any parameter that has
        // no row yet and refreshes the value of every row nobody chose (updated_at empty) to
        // this build's default. ⭐ Afterwards every value the read returns is already the
        // authoritative one, so there is no second code path here for "was this user-set".
        _store.Sync(Descriptions);

        var rows = _store.LoadAll();

        // Taken before any row is applied: these are the values an absent row stands for.
        foreach (var parameter in SettingsCatalog.Persisted)
            _defaults[parameter.Id] = parameter.Read!(model);

        foreach (var parameter in SettingsCatalog.Persisted)
        {
            if (!rows.TryGetValue(parameter.Id, out var raw)) continue;

            var updated = parameter.Write!(model, raw);

            if (updated is null)
            {
                // This build does not understand the stored text: a value removed from an enum,
                // a type that changed, a hand-edited typo. ⭐ This is P2-77's exact case, and the
                // decision (user, 2026-08-07) is to fall back to THIS ROW's default and keep
                // every other one.
                //
                // ⚠️ The row is deliberately left in the database rather than corrected. A later
                // build may understand it again, and rewriting the user's value with our default
                // would destroy the only evidence of what they had chosen.
                _logger.LogWarning(
                    "Settings row {Id} ({Name}) holds {Value}, which this build does not "
                    + "understand; using the default for it and keeping every other setting.",
                    parameter.Id, parameter.Name, raw);
                continue;
            }

            model = updated;
            _persisted[parameter.Id] = raw;
        }

        // Rows whose id is not in the catalog at all (a retired parameter from an older build)
        // are ignored on purpose and left in place — same reason as above, and the ids are never
        // reused so they can never be misread as something else.
        //
        // ⚠️ Counted by asking the catalog, NOT as "rows minus rows applied". The subtraction
        // was the first version and it is wrong: a row whose id IS known but whose value could
        // not be read is not applied either, so it was counted as unknown and reported twice
        // under two different names. Measured on the real database, not spotted by reading.
        var known = SettingsCatalog.Persisted.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = rows.Keys.Count(id => !known.Contains(id));

        if (unknown > 0)
            _logger.LogDebug("Settings store held {Count} row(s) unknown to this build.", unknown);

        return model;
    }

    /// <summary>
    /// Writes only the rows whose value differs from what the store is believed to hold.
    /// See the note on <see cref="_persisted"/> for why that matters beyond efficiency.
    /// </summary>
    private void Persist()
    {
        // ⭐ See _persistGate: the debounce timer and Flush() can both be in here at once, and
        // _persisted below is a plain Dictionary written outside _gate.
        lock (_persistGate)
        {
            PersistCore();
        }
    }

    private void PersistCore()
    {
        AppSettingsModel snapshot;
        lock (_gate) snapshot = _model;

        var changed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in SettingsCatalog.Persisted)
        {
            var current = parameter.Read!(snapshot);

            // An absent row stands for this build's default — see the note on _defaults.
            // ⚠️ A row that failed to parse is absent from _persisted too, so it is compared
            // against the default as well. That is deliberate: it means an unreadable value is
            // left alone unless the user actually picks something, rather than being quietly
            // overwritten with our default the first time anything else is saved.
            var known = _persisted.TryGetValue(parameter.Id, out var stored)
                ? stored
                : _defaults[parameter.Id];

            if (string.Equals(known, current, StringComparison.Ordinal)) continue;

            changed[parameter.Id] = current;
        }

        if (changed.Count == 0) return;

        _store.Upsert(changed, Descriptions);

        // ⚠️ Updated after the store call, and unconditionally. Upsert never throws; if it
        // failed it has already logged, and treating the write as done is the right trade —
        // the alternative retries every row on every later save, turning one failed write into
        // a permanent full-set write, which is the behaviour this design removes.
        foreach (var (id, value) in changed) _persisted[id] = value;
    }
}
