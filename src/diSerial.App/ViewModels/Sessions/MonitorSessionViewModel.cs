using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

// Manage a dual-port session.
public sealed partial class MonitorSessionViewModel : SessionViewModel
{
    // Initialize a monitoring session.
    public MonitorSessionViewModel(
        SerialChannelPair pair,
        SerialPortSettings settings,
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context)
        : base(capture, recorder, context, shortcuts: null)
    {
        Settings = settings;

        ChannelA = new ChannelViewModel(ChannelId.A, pair.ChannelA);
        ChannelB = new ChannelViewModel(ChannelId.B, pair.ChannelB);

        Subscribe(OnChannelPropertyChanged,
            h => ChannelA.PropertyChanged += h, h => ChannelA.PropertyChanged -= h);
        Subscribe(OnChannelPropertyChanged,
            h => ChannelB.PropertyChanged += h, h => ChannelB.PropertyChanged -= h);
        ApplyChannelPlaceholders();

        SendPanel.IsMonitorSession = true;
        SendPanel.IsSendEnabled = false;

        SendPanel.SendTargets.Add(ChannelA);
        SendPanel.SendTargets.Add(ChannelB);

        SendPanel.SelectedTarget = ChannelA;
        Subscribe(OnEnableSendRequested,
            h => SendPanel.EnableSendRequested += h, h => SendPanel.EnableSendRequested -= h);

        ApplyStoredPreferences();
    }

    public SerialPortSettings Settings { get; }

    // Describe the recorded batch.
    protected override RecordingBatchInfo DescribeRecordingBatch() => new(
        SessionKind.Monitor,
        ChannelA.PortName, ChannelB.PortName,
        ChannelA.Alias, ChannelB.Alias,
        Settings);

    // Build the export base name.
    protected override string DescribeExportBaseName(string kind) =>
        $"diserial-{SerialPortInfo.FileNameSegment(ChannelA.PortName)}" +
        $"-{SerialPortInfo.FileNameSegment(ChannelB.PortName)}" +
        $"-{Settings.ShortDescription.Replace(' ', '-')}-{DateTime.Now:yyyyMMdd-HHmmss}";

    public ChannelViewModel ChannelA { get; }

    public ChannelViewModel ChannelB { get; }



    public override SessionKind Kind => SessionKind.Monitor;

    public override string Title => LF(LocKeys.SessionMonitorTitle, ChannelA.Alias, ChannelB.Alias);

    public override string StatusText => LF(
        LocKeys.SessionMonitorStatus,
        Describe(ChannelA), ChannelA.BytesReceived,
        Describe(ChannelB), ChannelB.BytesReceived,
        Settings.ShortDescription,
        DescribeState(),
        LogPanel.FrameCount);

    // Format a port and alias label.
    private string Describe(ChannelViewModel channel) => channel.HasCustomAlias
        ? LF(LocKeys.ChannelPortWithAlias, channel.PortName, channel.Alias)
        : channel.PortName;

    // Refresh monitor channel labels.
    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChannelViewModel.Alias)) return;
        if (sender is not ChannelViewModel channel) return;

        LogPanel.RelabelChannel(channel.Id, channel.InlineLabel);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
    }

    // Update channel placeholders.
    private void ApplyChannelPlaceholders()
    {
        ChannelA.AliasPlaceholder = LF(LocKeys.ChannelAliasPlaceholder, ChannelA.PortName);
        ChannelB.AliasPlaceholder = LF(LocKeys.ChannelAliasPlaceholder, ChannelB.PortName);
    }

    // Refresh localized display text.
    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        ApplyChannelPlaceholders();
    }

    // Describe connection status.

    protected override string DescribePlainConnectionState() => State switch
    {
        ConnectionState.Connected => L(LocKeys.StateMonitoring),
        ConnectionState.Connecting => L(LocKeys.StateConnecting),
        ConnectionState.Faulted => L(LocKeys.StateFaulted),
        _ => L(LocKeys.StateNotStarted)
    };

    protected override string StartKey => LocKeys.ToolbarStartMonitoring;

    protected override string StopKey => LocKeys.ToolbarStopMonitoring;

    // Get a frame channel alias.
    protected override string? ResolveChannelAlias(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.HasCustomAlias ? ChannelA.Alias : null,
        ChannelId.B => ChannelB.HasCustomAlias ? ChannelB.Alias : null,
        _ => null
    };

    // Reset channel byte counters.
    protected override void OnCleared()
    {
        ChannelA.BytesReceived = 0;
        ChannelB.BytesReceived = 0;
    }

    // Get a frame channel port.
    protected override string? ResolveChannelPort(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.PortName,
        ChannelId.B => ChannelB.PortName,
        _ => null
    };

    // Get a frame channel label.
    protected override string? ResolveChannelLabel(ChannelId channel) => channel switch
    {
        ChannelId.A => ChannelA.InlineLabel,
        ChannelId.B => ChannelB.InlineLabel,
        _ => null
    };

    // Handle newly displayed frames.
    protected override void OnFramesAppended(
        IReadOnlyList<(SerialFrame Frame, string? Alias)> batch)
    {
        long a = 0, b = 0;

        foreach (var (frame, _) in batch)
        {
            if (frame.Direction != FrameDirection.Rx) continue;

            switch (frame.Channel)
            {
                case ChannelId.A: a += frame.Length; break;
                case ChannelId.B: b += frame.Length; break;
            }
        }

        ChannelA.BytesReceived += a;
        ChannelB.BytesReceived += b;
    }

    // Confirm monitor transmission.
    private async void OnEnableSendRequested(object? sender, EventArgs e)
    {
        var confirmed = await Context.DialogService.ShowConfirmationAsync(
            L(LocKeys.InjectionTitle),
            L(LocKeys.InjectionMessage),
            L(LocKeys.InjectionConfirm),
            L(LocKeys.CommonCancel));

        if (confirmed) SendPanel.ConfirmEnableSend();
    }
}
