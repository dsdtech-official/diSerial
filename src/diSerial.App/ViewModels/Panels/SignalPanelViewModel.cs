using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

// Manage serial control lines.
public sealed partial class SignalPanelViewModel : LocalizedViewModelBase
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IControlLineSession _session;
    private readonly IPeriodicTimer _poll;

    private bool _disposed;

    // Initialize control line polling.
    public SignalPanelViewModel(
        IControlLineSession session,
        IPeriodicTimerFactory timers,
        ILocalizationService localization)
        : base(localization)
    {
        _session = session;

        IsRtsOwnedByFlowControl = session.IsRtsOwnedByFlowControl;

        _poll = timers.Create(PollInterval, Poll, TimerPriority.Background);
        _poll.Start();

        Poll();
    }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _dtrAsserted;

    [ObservableProperty]
    private bool _rtsAsserted;

    [ObservableProperty]
    private bool _isRtsOwnedByFlowControl;

    [ObservableProperty]
    private ControlLineState _cts = ControlLineState.Unknown;

    [ObservableProperty]
    private ControlLineState _dsr = ControlLineState.Unknown;

    [ObservableProperty]
    private ControlLineState _dcd = ControlLineState.Unknown;

    public string CtsText => Describe(Cts);

    public string DsrText => Describe(Dsr);

    public string DcdText => Describe(Dcd);

    // Describe a control line state.
    private string Describe(ControlLineState state) => L(state switch
    {
        ControlLineState.High => LocKeys.SignalStateHigh,
        ControlLineState.Low => LocKeys.SignalStateLow,
        _ => LocKeys.SignalStateUnknown
    });

    public int PollCount { get; private set; }

    // Read control line states.
    private void Poll()
    {
        if (_disposed) return;

        PollCount++;

        var lines = _session.ReadControlLines();
        Cts = lines.Cts;
        Dsr = lines.Dsr;
        Dcd = lines.Dcd;
    }

    // Refresh the CTS indicator.
    partial void OnCtsChanged(ControlLineState value) => OnPropertyChanged(nameof(CtsText));

    // Refresh the DSR indicator.
    partial void OnDsrChanged(ControlLineState value) => OnPropertyChanged(nameof(DsrText));

    // Refresh the DCD indicator.
    partial void OnDcdChanged(ControlLineState value) => OnPropertyChanged(nameof(DcdText));

    // Apply the DTR output state.
    partial void OnDtrAssertedChanged(bool value) => Push(SerialOutputLine.Dtr, value);

    // Apply the RTS output state.
    partial void OnRtsAssertedChanged(bool value)
    {
        if (IsRtsOwnedByFlowControl) return;

        Push(SerialOutputLine.Rts, value);
    }

    // Apply an output line state.
    private void Push(SerialOutputLine line, bool asserted)
    {
        if (_disposed) return;

        _ = _session.SetOutputLineAsync(line, asserted);
    }

    // Stop control line polling.
    protected override void DisposeCore()
    {
        _disposed = true;

        _poll.Stop();
        _poll.Dispose();
    }
}
