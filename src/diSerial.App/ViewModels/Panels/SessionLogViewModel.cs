using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Panels;

// Render one session log event.
public sealed class SessionLogRowViewModel(string time, string message, LogLevel level)
{
    public string Time { get; } = time;

    public string Message { get; } = message;

    public bool IsWarning { get; } = level == LogLevel.Warning;

    public bool IsError { get; } = level >= LogLevel.Error;
}

// Present session log events.
public sealed partial class SessionLogViewModel : LocalizedViewModelBase
{
    public static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(250);

    private readonly SessionLogBuffer? _buffer;
    private readonly IPeriodicTimer? _drain;
    private DateTimeOffset? _origin;

    private long _lastSeen;
    private bool _disposed;

    // Start session log updates.
    public SessionLogViewModel(
        SessionLogBuffer? buffer,
        IPeriodicTimerFactory timers,
        ILocalizationService localization)
        : base(localization)
    {
        _buffer = buffer;

        if (buffer is null)
        {
            return;
        }

        var existing = buffer.Snapshot();
        _lastSeen = existing.Count == 0 ? -1 : existing[^1].Sequence;
        _droppedAtStart = buffer.DroppedCount;

        _drain = timers.Create(DrainInterval, Drain, TimerPriority.Background);
        _drain.Start();
    }

    private readonly int _droppedAtStart;

    public ObservableCollection<SessionLogRowViewModel> Entries { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _hasDropped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DroppedNotice))]
    private int _droppedCount;

    [ObservableProperty]
    private bool _autoScroll = true;

    public string DroppedNotice => LF("SessionLog.Dropped", DroppedCount);

    // Display pending session logs.
    private void Drain()
    {
        if (_disposed || _buffer is null) return;

        foreach (var entry in _buffer.Since(_lastSeen))
        {
            _lastSeen = entry.Sequence;
            Entries.Add(new SessionLogRowViewModel(Format(entry.Timestamp), entry.Message, entry.Level));
        }

        while (Entries.Count > _buffer.Capacity) Entries.RemoveAt(0);

        IsEmpty = Entries.Count == 0;

        var dropped = _buffer.DroppedCount - _droppedAtStart;
        DroppedCount = dropped < 0 ? 0 : dropped;
        HasDropped = DroppedCount > 0;
    }

    // Format elapsed session log time.
    private string Format(DateTimeOffset at)
    {
        _origin ??= at;

        var elapsed = at - _origin.Value;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    // Stop session log updates.
    protected override void DisposeCore()
    {
        _disposed = true;

        _drain?.Stop();
        _drain?.Dispose();
    }
}
