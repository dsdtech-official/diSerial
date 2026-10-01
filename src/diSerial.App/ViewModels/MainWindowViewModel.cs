using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiSerial.App.ViewModels;

// Manage the main window.
public sealed partial class MainWindowViewModel : LocalizedViewModelBase, IAsyncDisposable
{
    private readonly IDialogService _dialogService;
    private readonly ISessionViewModelFactory _sessionFactory;
    private readonly IDeviceWatcher _deviceWatcher;
    private readonly IRecordingCatalog? _recordingCatalog;
    private readonly ILogger _logger;
    private readonly ISerialPortFactory _serialPortFactory;
    private readonly IAppSettings _settings;

    private readonly Func<AppOptions, OptionsDialogViewModel> _optionsDialogFactory;
    private readonly Func<NewSessionDialogViewModel> _newSessionDialogFactory;
    private readonly Func<long?, RecordingManagerViewModel> _recordingManagerFactory;

    // Initialize main window services.
    public MainWindowViewModel(
        IDialogService dialogService,
        ISessionViewModelFactory sessionFactory,
        IDeviceWatcher deviceWatcher,
        ISerialPortFactory serialPortFactory,
        ILocalizationService localization,
        IAppSettings settings,
        ISessionTypeCatalog sessionTypes,
        Func<NewSessionDialogViewModel> newSessionDialogFactory,
        Func<long?, RecordingManagerViewModel> recordingManagerFactory,
        Func<AppOptions, OptionsDialogViewModel> optionsDialogFactory,
        DeveloperModeState developerMode,
        IRecordingCatalog? recordingCatalog = null,
        ILoggerFactory? loggerFactory = null)
        : base(localization)
    {
        _recordingCatalog = recordingCatalog;
        _logger = loggerFactory?.CreateLogger("Recording.Storage") ?? NullLogger.Instance;
        IsDeveloperMode = developerMode.IsEnabled;
        _dialogService = dialogService;
        _sessionFactory = sessionFactory;
        _deviceWatcher = deviceWatcher;
        _serialPortFactory = serialPortFactory;
        _settings = settings;
        _newSessionDialogFactory = newSessionDialogFactory;
        _recordingManagerFactory = recordingManagerFactory;
        _optionsDialogFactory = optionsDialogFactory;

        SessionTypes = sessionTypes.CreateItems();

        foreach (var type in SessionTypes)
        {
            type.Choose = NewSessionCommand;
        }

        LanguageMenuItems = localization.AvailableLanguages
            .Select(o => new LanguageMenuItemViewModel(o))
            .ToArray();

        SelectedLanguage = localization.AvailableLanguages
            .FirstOrDefault(l => l.Culture.Name == localization.CurrentCulture.Name)
            ?? localization.AvailableLanguages[0];

        SyncLanguageSelection();
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PauseMenuText))]
    [NotifyPropertyChangedFor(nameof(NewSessionMenuText))]
    [NotifyCanExecuteChangedFor(nameof(CloseSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private SessionViewModel? _activeSession;

    public bool HasActiveSession => ActiveSession is not null;

    private bool CanCreateSession => !HasActiveSession;

    public IReadOnlyList<SessionTypeItem> SessionTypes { get; }


    public string? SessionError => ActiveSession?.ErrorNotice;

    public bool HasSessionError => ActiveSession?.HasErrorNotice ?? false;

    // Clear the visible session error.
    [RelayCommand(CanExecute = nameof(HasSessionError))]
    private void DismissSessionError() => ActiveSession?.DismissErrorCommand.Execute(null);

    // Toggle the display pause state.
    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private void TogglePause() => ActiveSession?.TogglePauseCommand.Execute(null);

    // Detach the active session.
    partial void OnActiveSessionChanging(SessionViewModel? oldValue, SessionViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnActiveSessionPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnActiveSessionPropertyChanged;
    }

    // Bind the active session state.
    partial void OnActiveSessionChanged(SessionViewModel? value) => RaiseSessionErrorChanged();

    // Refresh active session controls.
    private void OnActiveSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var everything = e.PropertyName is null or "";

        if (everything || e.PropertyName is nameof(SessionViewModel.ErrorNotice)
                                         or nameof(SessionViewModel.HasErrorNotice))
        {
            RaiseSessionErrorChanged();
        }

        if (everything || e.PropertyName == nameof(SessionViewModel.Title))
        {
            OnPropertyChanged(nameof(Title));
        }

        if (everything || e.PropertyName == nameof(SessionViewModel.PauseMenuText))
        {
            OnPropertyChanged(nameof(PauseMenuText));
        }
    }

    // Notify visible session errors.
    private void RaiseSessionErrorChanged()
    {
        OnPropertyChanged(nameof(SessionError));
        OnPropertyChanged(nameof(HasSessionError));
        DismissSessionErrorCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<LanguageMenuItemViewModel> LanguageMenuItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageMenuHeader))]
    private LanguageOption _selectedLanguage;

    public string LanguageMenuHeader => LF(LocKeys.MenuLanguage, SelectedLanguage.NativeName);

    public string? PlatformWarning =>
        PlatformStatusPresenter.Describe(_serialPortFactory.SupportStatus, Localization);

    public bool HasPlatformWarning => PlatformWarning is not null;

    [ObservableProperty]
    private string? _storageNotice;

    public bool HasStorageNotice => StorageNotice is not null;

    // Refresh the storage notice.
    partial void OnStorageNoticeChanged(string? value) => OnPropertyChanged(nameof(HasStorageNotice));

    public const long StorageNoticeThresholdBytes = 1024L * 1024 * 1024;

    // Check recording storage.
    internal async Task CheckRecordingStorageAsync()
    {
        if (_recordingCatalog is null) return;

        try
        {
            var bytes = await _recordingCatalog.GetDatabaseSizeAsync();
            if (bytes < StorageNoticeThresholdBytes) return;

            StorageNotice = LF(
                LocKeys.StorageNotice,
                RecordingManagerViewModel.FormatSize(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not measure the recording database.");
        }
    }

    // Clear the storage notice.
    [RelayCommand]
    private void DismissStorageNotice() => StorageNotice = null;


    public string Title
    {
        get
        {
            var title = ActiveSession is null
                ? L(LocKeys.AppName)
                : LF(LocKeys.AppTitleWithSession, ActiveSession.Title);

            return IsDeveloperMode ? LF(LocKeys.AppTitleDeveloperMode, title) : title;
        }
    }

    public string PauseMenuText =>
        ActiveSession?.PauseMenuText ?? L(LocKeys.MenuSessionPause);

    public string NewSessionMenuText =>
        L(HasActiveSession ? LocKeys.MenuFileNewSessionBlocked : LocKeys.MenuFileNewSession);

    public bool IsDeveloperMode { get; }

    // Start port and storage checks.

    public async Task InitializeAsync()
    {
        await _deviceWatcher.StartAsync();

        await CheckRecordingStorageAsync();
    }

    // Change the active UI language.
    [RelayCommand]
    private void SetLanguage(LanguageOption? option)
    {
        if (option is null) return;

        SelectedLanguage = option;
        SyncLanguageSelection();
        Localization.SetLanguage(option.Culture);
        _settings.Language = option.Culture.Name;
    }

    // Refresh localized display text.
    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();

        foreach (var type in SessionTypes) type.RefreshText();
    }

    // Sync the selected UI language.
    private void SyncLanguageSelection()
    {
        foreach (var item in LanguageMenuItems)
        {
            item.IsSelected = item.Option.Culture.Name == SelectedLanguage.Culture.Name;
        }
    }

    // Configure and open a session.
    [RelayCommand(CanExecute = nameof(CanCreateSession))]
    private async Task NewSessionAsync(SessionTypeItem? startAt)
    {
        if (HasActiveSession) return;

        using var dialog = _newSessionDialogFactory();

        dialog.PreselectType(startAt);

        await dialog.LoadAsync();
        await OpenSessionFromDialogAsync(dialog);
    }

    // Close the active session.
    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private async Task CloseSessionAsync()
    {
        if (ActiveSession is not { } session) return;

        if (session.IsRecording)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                L(LocKeys.CloseWhileRecordingTitle),
                L(LocKeys.CloseWhileRecordingMessage),
                L(LocKeys.CloseWhileRecordingConfirm),
                L(LocKeys.CommonCancel));

            if (!confirmed) return;
        }

        Sessions.Remove(session);
        await session.DisposeAsync();

        ActiveSession = null;
    }

    // Show recording management.
    [RelayCommand]
    private async Task ShowRecordingsAsync()
    {
        using var dialog = _recordingManagerFactory(ActiveSession?.RecordingBatchId);

        await dialog.RefreshAsync();
        await _dialogService.ShowRecordingManagerAsync(dialog);
    }

    // Show application options.
    [RelayCommand]
    private async Task ShowOptionsAsync()
    {
        var dialog = _optionsDialogFactory(_settings.Options);

        if (await _dialogService.ShowOptionsDialogAsync(dialog) is { } chosen)
        {
            _settings.Options = chosen;
        }
    }

    // Show application information.
    [RelayCommand]
    private async Task ShowAboutAsync() =>
        await _dialogService.ShowMessageAsync(
            L(LocKeys.AboutTitle),
            LF(LocKeys.AboutMessage, AppInfo.DisplayVersion));

    // Open the selected session.
    private async Task OpenSessionFromDialogAsync(NewSessionDialogViewModel dialog)
    {
        var result = await _dialogService.ShowNewSessionDialogAsync(dialog);
        if (result is null) return;

        if (dialog.CreatedSession is not { } session) return;

        Sessions.Add(session);
        ActiveSession = session;
    }

    // Release owned resources.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (ActiveSession is { } active) active.PropertyChanged -= OnActiveSessionPropertyChanged;

        Dispose();

        foreach (var session in Sessions.ToArray())
        {
            await session.DisposeAsync();
        }
        Sessions.Clear();

        await _deviceWatcher.StopAsync();
    }

    private int _disposed;
}
