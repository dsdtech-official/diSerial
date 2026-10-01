using DiSerial.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.Services;

public interface IAppSettings
{
    string? Language { get; set; }

    string? LastExportDirectory { get; set; }

    AppOptions Options { get; set; }

    SessionPreferences Terminal { get; set; }

    SessionPreferences Monitor { get; set; }


    // Write pending settings now.
    void Flush();
}

// Persist application settings.
public sealed class StoredAppSettings : IAppSettings, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsStore _store;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private readonly Lock _persistGate = new();

    private readonly Dictionary<string, string> _persisted = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);

    private AppSettingsModel _model;
    private bool _disposed;

    // Load persisted app settings.
    public StoredAppSettings(ISettingsStore store, ILogger<StoredAppSettings>? logger = null)
    {
        _store = store;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _model = Load();

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

    public AppOptions Options
    {
        get { lock (_gate) return _model.Options; }
        set => Mutate(m => m with { Options = value });
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

    // Write pending settings now.
    public void Flush()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Persist();
    }

    // Flush settings and stop timers.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Flush();
        _timer.Dispose();
    }

    // Queue saving after changes.
    private void Mutate(Func<AppSettingsModel, AppSettingsModel> change)
    {
        lock (_gate) _model = change(_model);

        _logger.LogDebug("Settings change queued; write scheduled in {Delay} ms.", SaveDelay.TotalMilliseconds);

        _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private static readonly IReadOnlyDictionary<string, SettingsRowInfo> Descriptions =
        SettingsCatalog.Persisted.ToDictionary(
            p => p.Id,
            p => new SettingsRowInfo(p.Label, p.ValueType, SettingsCatalog.DefaultValueOf(p)),
            StringComparer.Ordinal);

    // Save settings and log failures.
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

    // Load stored values.
    private AppSettingsModel Load()
    {
        var model = new AppSettingsModel();

        _store.Sync(Descriptions);

        var rows = _store.LoadAll();

        foreach (var parameter in SettingsCatalog.Persisted)
            _defaults[parameter.Id] = parameter.Read!(model);

        foreach (var parameter in SettingsCatalog.Persisted)
        {
            if (!rows.TryGetValue(parameter.Id, out var raw)) continue;

            var updated = parameter.Write!(model, raw);

            if (updated is null)
            {
                _logger.LogWarning(
                    "Settings row {Id} ({Name}) holds {Value}, which this build does not "
                    + "understand; using the default for it and keeping every other setting.",
                    parameter.Id, parameter.Name, raw);
                continue;
            }

            model = updated;
            _persisted[parameter.Id] = raw;
        }

        var known = SettingsCatalog.Persisted.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = rows.Keys.Count(id => !known.Contains(id));

        if (unknown > 0)
            _logger.LogDebug("Settings store held {Count} row(s) unknown to this build.", unknown);

        return model;
    }

    // Save the current values.
    private void Persist()
    {
        lock (_persistGate)
        {
            PersistCore();
        }
    }

    // Write the settings snapshot.
    private void PersistCore()
    {
        AppSettingsModel snapshot;
        lock (_gate) snapshot = _model;

        var changed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in SettingsCatalog.Persisted)
        {
            var current = parameter.Read!(snapshot);

            var known = _persisted.TryGetValue(parameter.Id, out var stored)
                ? stored
                : _defaults[parameter.Id];

            if (string.Equals(known, current, StringComparison.Ordinal)) continue;

            changed[parameter.Id] = current;
        }

        if (changed.Count == 0) return;

        _store.Upsert(changed, Descriptions);

        foreach (var (id, value) in changed) _persisted[id] = value;
    }
}
