using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Converters;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.ViewModels.Dialogs;

// Manage saved recordings.
public sealed partial class RecordingManagerViewModel : LocalizedViewModelBase
{
    private readonly IRecordingCatalog _catalog;
    private readonly IRecordingReader _reader;
    private readonly IExportService _export;
    private readonly IDialogService _dialogs;
    private readonly IEnumChoiceProvider _enumChoices;
    private readonly IAppSettings _settings;
    private readonly ILogger _logger;

    private readonly long? _liveBatchId;

    // Initialize recording management.
    public RecordingManagerViewModel(
        IRecordingCatalog catalog,
        IRecordingReader reader,
        IExportService export,
        IDialogService dialogs,
        IEnumChoiceProvider enumChoices,
        IAppSettings settings,
        ILocalizationService localization,
        long? liveBatchId = null,
        ILogger? logger = null)
        : base(localization)
    {
        _catalog = catalog;
        _reader = reader;
        _export = export;
        _dialogs = dialogs;
        _enumChoices = enumChoices;
        _settings = settings;
        _liveBatchId = liveBatchId;
        _logger = logger ?? NullLogger.Instance;

        DateText = new UiCultureDateConverter(localization);

        (From, To) = DefaultRange(DateTimeOffset.Now);
    }


    public ObservableCollection<RecordingBatchItemViewModel> Batches { get; } = [];

    public bool IsEmpty => Batches.Count == 0;

    [ObservableProperty]
    private string _loadError = string.Empty;

    public bool HasLoadError => LoadError.Length > 0;

    public bool ShowsEmptyState => IsEmpty && !HasLoadError;

    // Refresh the query error display.
    partial void OnLoadErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(ShowsEmptyState));
    }

    [ObservableProperty]
    private bool _isBusy;

    // Refresh operation availability.
    partial void OnIsBusyChanged(bool value) => NotifyCommands();


    [ObservableProperty]
    private DateTime? _from;

    [ObservableProperty]
    private DateTime? _to;

    public UiCultureDateConverter DateText { get; }

    public string DateHint => DateText.Hint;

    // Get the initial recording dates.
    internal static (DateTime From, DateTime To) DefaultRange(DateTimeOffset now)
    {
        var startOfToday = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return (startOfToday.AddDays(-2), startOfToday);
    }


    // Reload recorded batch summaries.
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var filter = new RecordingBatchFilter
            {
                From = From,

                To = To?.Date.AddDays(1)
            };

            var items = await _catalog.ListBatchesAsync(filter);

            Batches.Clear();
            foreach (var item in items)
            {
                Batches.Add(new RecordingBatchItemViewModel(
                    item, Localization, item.Id == _liveBatchId, OnSelectionChanged));
            }

            DatabaseSize = FormatSize(await _catalog.GetDatabaseSizeAsync());

            LoadError = string.Empty;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsEmptyState));
            NotifyCommands();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the recording catalogue.");
            LoadError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Delete selected batches.
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var ids = Selected().Select(b => b.Id).ToArray();
        if (ids.Length == 0) return;

        var confirmed = await _dialogs.ShowConfirmationAsync(
            L(LocKeys.RecordingsDeleteTitle),
            LF(LocKeys.RecordingsDeleteMessage, ids.Length),
            L(LocKeys.RecordingsDelete),
            L(LocKeys.CommonCancel));

        if (!confirmed) return;

        await RunAsync(async () => await _catalog.DeleteBatchesAsync(ids));
    }

    // Compact the recording database.
    [RelayCommand(CanExecute = nameof(CanCompact))]
    private async Task CompactAsync()
    {
        string message;

        IsBusy = true;
        try
        {
            var before = await _catalog.GetDatabaseSizeAsync();
            await _catalog.VacuumAsync();
            var after = await _catalog.GetDatabaseSizeAsync();

            message = LF(LocKeys.RecordingsCompacted, FormatSize(before), FormatSize(after));
        }
        catch (RecordingDatabaseBusyException ex)
        {
            _logger.LogWarning(ex, "Compacting the recording database: it is in use.");
            message = L(LocKeys.RecordingsCompactBusy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compacting the recording database failed.");
            message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
        await _dialogs.ShowMessageAsync(L(LocKeys.RecordingsCompact), message);
    }

    // Export the selected frame data.
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var item = Selected().SingleOrDefault();
        if (item is null) return;

        var summary = await _reader.GetBatchAsync(item.Id);
        if (summary is null) return;

        var prefs = item.Model.SessionKind == SessionKind.Monitor
            ? _settings.Monitor
            : _settings.Terminal;

        var channels = item.Model.SessionKind == SessionKind.Monitor
            ? new ExportChannels(
                item.Model.PortA, CustomAliasOnly(item.Model.PortA, item.Model.AliasA),
                item.Model.PortB, CustomAliasOnly(item.Model.PortB, item.Model.AliasB))
            : default;

        using var dialog = new ExportDialogViewModel(
            Localization,
            _enumChoices,
            _dialogs,
            new ExportDialogSeed(
                RecordingExportName.ForBatch(summary),
                prefs.Display.Format,
                prefs.Display.Timestamp,
                prefs.Display.ShowChannel,
                prefs.Display.ShowDelta,
                channels,
                ResolveExportDirectory()));

        var request = await _dialogs.ShowExportDialogAsync(dialog);
        if (request is null) return;

        IsBusy = true;
        try
        {
            var frames = new List<SerialFrame>();
            await foreach (var frame in _reader.ReadBatchAsync(item.Id)) frames.Add(frame);

            await _export.ExportAsync(frames, request.FilePath, request.Options);
            _logger.LogInformation(
                "Exported {Count} frames from batch {BatchId} to {Path}",
                frames.Count, item.Id, request.FilePath);

            var directory = Path.GetDirectoryName(request.FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) _settings.LastExportDirectory = directory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export of batch {BatchId} to {Path} failed.", item.Id, request.FilePath);
            await _dialogs.ShowMessageAsync(L(LocKeys.RecordingsDeleteTitle), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Close the current dialog.
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? CloseRequested;


    // Check deletion eligibility.
    private bool CanDelete() => !IsBusy && Selected().Any();

    // Check compaction eligibility.
    private bool CanCompact() => !IsBusy && _liveBatchId is null;

    // Check export eligibility.
    private bool CanExport()
    {
        if (IsBusy) return false;

        var selected = Selected().ToArray();
        return selected.Length == 1 && !selected[0].IsEmpty && !selected[0].IsLive;
    }

    public bool IsRecordingInProgress => _liveBatchId is not null;

    public bool ExportTakesOneBatchAtATime => Selected().Count() > 1;

    public bool MoreRowsCanBeSelected =>
        Selected().Count() == 1 && Batches.Count(b => !b.IsLive) > 1;

    public string SelectSeveralText =>
        LF(LocKeys.RecordingsSelectSeveral, MultiSelectModifier(OperatingSystem.IsMacOS()));

    // Choose the multiselect modifier.
    internal static string MultiSelectModifier(bool isMacOs) => isMacOs ? "⌘" : "Ctrl";


    // Select only the requested batch.
    internal void SelectOnly(RecordingBatchItemViewModel row)
    {
        if (row.IsLive) return;
        if (row.IsSelected) return;

        foreach (var batch in Batches) batch.IsSelected = false;
        row.IsSelected = true;
    }

    // Get selected inactive batches.
    private IEnumerable<RecordingBatchItemViewModel> Selected() =>
        Batches.Where(b => b.IsSelected && !b.IsLive);

    // Refresh selection commands.
    private void OnSelectionChanged() => NotifyCommands();

    // Refresh command availability.
    private void NotifyCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        CompactCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(ExportTakesOneBatchAtATime));

        OnPropertyChanged(nameof(MoreRowsCanBeSelected));
    }

    // Run a batch operation safely.
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A recording-manager operation failed.");
            await _dialogs.ShowMessageAsync(L(LocKeys.RecordingsTitle), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    // Exclude default port aliases.
    private static string? CustomAliasOnly(string? port, string? alias) =>
        string.IsNullOrWhiteSpace(alias) || string.Equals(alias, port, StringComparison.Ordinal)
            ? null
            : alias;

    // Choose the export directory.
    private string? ResolveExportDirectory()
    {
        var remembered = _settings.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(remembered)) return null;

        try
        {
            return Directory.Exists(remembered) ? remembered : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check the remembered export directory {Path}.", remembered);
            return null;
        }
    }

    // Refresh localized display text.
    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();

        OnPropertyChanged(nameof(DateHint));

        foreach (var batch in Batches) batch.RefreshText();
    }

    [ObservableProperty]
    private string _databaseSize = string.Empty;

    public string DatabaseSizeText =>
        DatabaseSize.Length == 0 ? string.Empty : LF(LocKeys.RecordingsDatabaseSize, DatabaseSize);

    // Refresh the database size text.
    partial void OnDatabaseSizeChanged(string value) => OnPropertyChanged(nameof(DatabaseSizeText));

    // Format a storage size label.
    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} {units[0]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:F1} {units[unit]}");
    }
}

// Represent a recording batch.
public sealed partial class RecordingBatchItemViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly Action _selectionChanged;

    // Initialize a batch list item.
    internal RecordingBatchItemViewModel(
        RecordingBatchListItem model,
        ILocalizationService localization,
        bool isLive,
        Action selectionChanged)
    {
        Model = model;
        _localization = localization;
        _selectionChanged = selectionChanged;
        IsLive = isLive;
    }

    public RecordingBatchListItem Model { get; }

    public long Id => Model.Id;

    public bool IsEmpty => Model.FrameCount == 0;

    public bool IsLive { get; }

    [ObservableProperty]
    private bool _isSelected;

    // Notify recording selection.
    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    public string SessionText => _localization[
        Model.SessionKind == SessionKind.Monitor
            ? LocKeys.DialogNewSessionMonitor
            : LocKeys.DialogNewSessionTerminal];

    public string PortText => Model.PortB is null
        ? Describe(Model.PortA, Model.AliasA)
        : _localization.Format(
            LocKeys.RecordingsPortPair,
            Describe(Model.PortA, Model.AliasA),
            Describe(Model.PortB, Model.AliasB));

    public string SettingsText => Model.Settings.ShortDescription.Replace(' ', '-');

    public string StartedText =>
        Model.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string DurationText
    {
        get
        {
            if (Model.EndedAt is not { } ended) return string.Empty;

            var span = ended - Model.StartedAt;
            if (span < TimeSpan.Zero) return string.Empty;

            return span.TotalDays >= 1
                ? span.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)
                : span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    public string FramesText => Model.FrameCount.ToString(CultureInfo.InvariantCulture);

    public string StateText
    {
        get
        {
            if (IsLive) return _localization[LocKeys.RecordingsStateRecording];
            if (Model.EndedAt is null) return _localization[LocKeys.RecordingsStateUnfinished];
            return IsEmpty ? _localization[LocKeys.RecordingsStateEmpty] : string.Empty;
        }
    }

    // Refresh localized labels.
    internal void RefreshText()
    {
        OnPropertyChanged(nameof(SessionText));
        OnPropertyChanged(nameof(StateText));
    }

    // Format a port and alias label.
    private string Describe(string port, string? alias) =>
        string.IsNullOrWhiteSpace(alias) || string.Equals(alias, port, StringComparison.Ordinal)
            ? port
            : _localization.Format(LocKeys.ChannelPortWithAlias, port, alias);
}
