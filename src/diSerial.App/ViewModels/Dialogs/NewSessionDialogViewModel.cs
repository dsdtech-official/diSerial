using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Configure a new session.
public sealed partial class NewSessionDialogViewModel : LocalizedViewModelBase
{
    private readonly IPortEnumerator _portEnumerator;
    private readonly IDeviceWatcher _deviceWatcher;
    private readonly ISessionViewModelFactory _sessionFactory;

    private IReadOnlyList<SerialPortInfo> _ports = [];

    // Initialize new session choices.
    public NewSessionDialogViewModel(
        IPortEnumerator portEnumerator,
        IDeviceWatcher deviceWatcher,
        ILocalizationService localization,
        ISessionTypeCatalog sessionTypes,
        ISessionViewModelFactory sessionFactory)
        : base(localization)
    {
        _portEnumerator = portEnumerator;
        _deviceWatcher = deviceWatcher;
        _sessionFactory = sessionFactory;

        SessionTypes = sessionTypes.CreateItems();

        foreach (var type in SessionTypes)
        {
            type.Choose = ChooseTypeCommand;
        }

        SelectedType = SessionTypes[0];
    }

    public IReadOnlyList<SessionTypeItem> SessionTypes { get; }

    [ObservableProperty]
    private SessionTypeItem? _selectedType;

    [ObservableProperty]
    private SessionConfigViewModel? _currentConfig;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingType))]
    private bool _isConfiguring;

    public bool IsChoosingType => !IsConfiguring;

    public NewSessionResult? Result { get; private set; }

    public SessionViewModel? CreatedSession { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenError))]
    private string? _openError;

    public bool HasOpenError => !string.IsNullOrEmpty(OpenError);

    public event EventHandler<bool>? CloseRequested;

    // Select the initial session type.
    public void PreselectType(SessionTypeItem? preselected)
    {
        if (preselected is null) return;

        var mine = SessionTypes.FirstOrDefault(t => t.Kind == preselected.Kind);
        if (mine is null) return;

        SelectedType = mine;
        IsConfiguring = true;
    }

    // Load available serial ports.
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Apply(await _portEnumerator.GetPortsAsync(cancellationToken));

        _deviceWatcher.PortsChanged += OnPortsChanged;
    }

    // Release port watcher bindings.
    protected override void DisposeCore()
    {
        _deviceWatcher.PortsChanged -= OnPortsChanged;
        DetachConfig(CurrentConfig);
    }

    // Update available port choices.
    private void OnPortsChanged(object? sender, PortsChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => Apply(e.Current));

    // Apply the available port list.
    private void Apply(IReadOnlyList<SerialPortInfo> ports)
    {
        _ports = ports;
        CurrentConfig?.ApplyPorts(ports);
    }

    // Choose a session configuration.
    [RelayCommand]
    private void ChooseType(SessionTypeItem? item)
    {
        if (item is null) return;

        SelectedType = item;
        IsConfiguring = true;
    }


    // Open the configured session.
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (CurrentConfig is not { } config) return;

        OpenError = null;
        config.RememberSettings();

        var request = config.BuildRequest();
        var session = _sessionFactory.Create(request);

        if (await session.TryConnectAsync() is { } failure)
        {
            await session.DisposeAsync();

            OpenError = config.DescribeFailure(failure);
            return;
        }

        CreatedSession = session;
        Result = request;
        CloseRequested?.Invoke(this, true);
    }

    // Check whether values are valid.
    private bool CanConfirm() => IsConfiguring && CurrentConfig?.CanConfirm is true;

    // Cancel the current dialog.
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, false);
    }

    // Refresh session type selection.
    partial void OnSelectedTypeChanged(SessionTypeItem? value)
    {
        DetachConfig(CurrentConfig);

        var config = value?.CreateConfig();
        if (config is not null)
        {
            config.CanConfirmChanged += OnConfigCanConfirmChanged;
            config.ApplyPorts(_ports);
        }

        CurrentConfig = config;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    // Refresh configuration controls.
    partial void OnIsConfiguringChanged(bool value) => ConfirmCommand.NotifyCanExecuteChanged();

    // Refresh confirmation access.
    private void OnConfigCanConfirmChanged(object? sender, EventArgs e)
        => ConfirmCommand.NotifyCanExecuteChanged();

    // Release configuration bindings.
    private void DetachConfig(SessionConfigViewModel? config)
    {
        if (config is null) return;

        config.CanConfirmChanged -= OnConfigCanConfirmChanged;
        config.Dispose();
    }
}
