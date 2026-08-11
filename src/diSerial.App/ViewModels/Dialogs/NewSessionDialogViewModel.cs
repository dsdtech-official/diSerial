using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// New-session dialog -- <b>two steps</b> (user decision 2026-08-03):
/// <b>1.</b> choose the session type, <b>2.</b> configure that type.
///
/// <para><b>Why two steps.</b> The two types need different port UI -- one port versus two --
/// and they used to share one screen, one ViewModel and one result record. Committing to the
/// type first gives step 2 a slot that is replaced wholesale per type, so each type's
/// configuration is written, read and changed on its own.</para>
///
/// <para>⭐ <b>This class names no session type, and that is the point.</b> It iterates
/// <see cref="ISessionTypeCatalog"/> and talks to whatever <see cref="SessionConfigViewModel"/>
/// comes back. <b>Adding a session type must not require editing this file or its .axaml</b> --
/// <c>NewSessionDialogDecouplingTests</c> enforces it, because a rule like this survives only
/// as long as something fails when it is broken.</para>
///
/// <para><b>Type is chosen at creation, not switched later in the main window.</b> A mode
/// switch leaves users unsure which mode they are in, and makes it impossible to have a
/// terminal and a monitor open at once.</para>
///
/// <para>⛔ <b>"Enter creates a default terminal session" was dropped</b> (user decision
/// 2026-08-03). It cannot survive two steps -- Enter on step 1 has to mean "next" -- and the
/// user judged the shortcut unnecessary under this design.</para>
/// </summary>
public sealed partial class NewSessionDialogViewModel : LocalizedViewModelBase
{
    private readonly IPortEnumerator _portEnumerator;
    private readonly IDeviceWatcher _deviceWatcher;
    private readonly ISessionViewModelFactory _sessionFactory;

    private IReadOnlyList<SerialPortInfo> _ports = [];

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

        // The first entry is the default, so a single serial port stays the shortest path.
        SelectedType = SessionTypes[0];
    }

    public IReadOnlyList<SessionTypeItem> SessionTypes { get; }

    /// <summary>
    /// The card chosen on step 1.
    ///
    /// ⚠ Changing it <b>replaces</b> <see cref="CurrentConfig"/>, discarding whatever was
    /// selected for the previous type. Going back and forth without changing the type keeps
    /// the configuration, which is why the config is built here and not on "next".
    /// </summary>
    [ObservableProperty]
    private SessionTypeItem? _selectedType;

    /// <summary>The chosen type's configuration step. Null only before a type is selected.</summary>
    [ObservableProperty]
    private SessionConfigViewModel? _currentConfig;

    /// <summary>false = choosing a type (step 1), true = configuring it (step 2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingType))]
    private bool _isConfiguring;

    public bool IsChoosingType => !IsConfiguring;

    /// <summary>The dialog result. Null means cancelled, or the port would not open.</summary>
    public NewSessionResult? Result { get; private set; }

    /// <summary>
    /// The session, already created <b>and connected</b>, for the caller to take over.
    ///
    /// ⚠ <b>The ports were opened inside this dialog</b> and the session keeps using them --
    /// the caller must <b>not</b> call <c>ConnectCommand</c>, which means "reconnect".
    /// </summary>
    public SessionViewModel? CreatedSession { get; private set; }

    /// <summary>
    /// Shown on step 2 when the last attempt could not open the port. Non-empty means
    /// "that attempt failed".
    ///
    /// Separate from the top-of-window banner (P0-2), and they never overlap: this one serves
    /// "the session does not exist yet", that one serves "an existing session broke".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenError))]
    private string? _openError;

    public bool HasOpenError => !string.IsNullOrEmpty(OpenError);

    /// <summary>Asks the view to close. Subscribed by the window.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// Starts on step 2 with <paramref name="preselected"/>'s type already chosen -- what the
    /// empty state's cards feed in (2026-08-03, user decision). A null argument leaves the
    /// dialog on step 1, which is what the File menu wants.
    ///
    /// <para>⚠ <b>The argument comes from someone else's list, so it is matched, not stored.</b>
    /// Each caller builds its own items off the catalog; assigning a foreign instance to
    /// <see cref="SelectedType"/> would leave the step 1 <c>ListBox</c> with a selection that is
    /// not one of its own items, so going Back would show no card selected.</para>
    ///
    /// <para>⚠ Must run <b>before</b> <see cref="LoadAsync"/> only in the sense that it costs
    /// nothing to; it is independent of the port list.</para>
    /// </summary>
    public void PreselectType(SessionTypeItem? preselected)
    {
        if (preselected is null) return;

        var mine = SessionTypes.FirstOrDefault(t => t.Kind == preselected.Kind);
        if (mine is null) return;

        SelectedType = mine;
        IsConfiguring = true;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Apply(await _portEnumerator.GetPortsAsync(cancellationToken));

        // Plugging or unplugging while the dialog is open updates the dropdowns immediately,
        // no need to close and reopen (C-03a). Unsubscribed in DisposeCore -- the caller holds
        // this VM in a using, see MainWindowViewModel.
        _deviceWatcher.PortsChanged += OnPortsChanged;
    }

    protected override void DisposeCore()
    {
        _deviceWatcher.PortsChanged -= OnPortsChanged;
        DetachConfig(CurrentConfig);
    }

    private void OnPortsChanged(object? sender, PortsChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => Apply(e.Current));

    private void Apply(IReadOnlyList<SerialPortInfo> ports)
    {
        _ports = ports;
        CurrentConfig?.ApplyPorts(ports);
    }

    [RelayCommand]
    private void SelectType(SessionTypeItem? item)
    {
        if (item is not null) SelectedType = item;
    }

    /// <summary>Step 1 → step 2.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => IsConfiguring = true;

    private bool CanGoNext() => CurrentConfig is not null;

    // ⛔ There used to be a Back command here (step 2 → step 1). Removed 2026-08-03 by user
    // decision: Cancel closes the window, and reopening costs one click, because BOTH entry
    // points already own the type choice -- the empty state has the cards on screen, and the
    // File menu starts on step 1. Back was a third route to a choice that is never far away.
    //
    // ⚠️ Do not reinstate it without re-checking that pairing. It is what makes the type
    // reachable when a session is open and the empty state is therefore hidden.

    /// <summary>
    /// Confirm -- <b>opens the port right here</b>, and only closes if that succeeds.
    ///
    /// <para>⚠ <b>On failure the dialog stays on step 2</b>, with the message in
    /// <see cref="OpenError"/> and every selection still in place, so the user can change one
    /// port and retry (spec 4.7).</para>
    ///
    /// <para><b>Why not probe, close, then open normally:</b> closing a port measures 1.4-2
    /// seconds, so probing would freeze the button for three; and between probe and real open
    /// there is a race where another process can take the port. The port opened here is handed
    /// straight to the session instead.</para>
    /// </summary>
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
            // Releases the session just built -- it holds an already-open port (the monitor's
            // other channel has been rolled back) and a recorder. Without this the port stays
            // taken by this very process, and the retry hits "port in use" against itself.
            await session.DisposeAsync();

            OpenError = config.DescribeFailure(failure);
            return;   // ⬅ deliberately no CloseRequested: the dialog stays put
        }

        CreatedSession = session;
        Result = request;
        CloseRequested?.Invoke(this, true);
    }

    private bool CanConfirm() => IsConfiguring && CurrentConfig?.CanConfirm is true;

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, false);
    }

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
        NextCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConfiguringChanged(bool value) => ConfirmCommand.NotifyCanExecuteChanged();

    private void OnConfigCanConfirmChanged(object? sender, EventArgs e)
        => ConfirmCommand.NotifyCanExecuteChanged();

    private void DetachConfig(SessionConfigViewModel? config)
    {
        if (config is null) return;

        config.CanConfirmChanged -= OnConfigCanConfirmChanged;
        config.Dispose();
    }
}
