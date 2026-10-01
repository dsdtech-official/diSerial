using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.App.ViewModels.Panels;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Sessions;

// Manage a terminal session.
public sealed partial class TerminalSessionViewModel : SessionViewModel
{
    // Initialize a terminal session.
    public TerminalSessionViewModel(
        SerialPortInfo port,
        SerialPortSettings settings,
        ICaptureSession capture,
        ISessionRecorder recorder,
        SessionContext context)
        : base(capture, recorder, context, context.Shortcuts)
    {
        Port = port;
        Settings = settings;

        SendPanel.IsMonitorSession = false;
        SendPanel.IsSendEnabled = true;


        AutoAnswerPanel = new AutoAnswerPanelViewModel(
            context.AutoAnswer, context.LoggerFactory.CreateLogger("AutoAnswer"));

        AutoAnswerPanel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AutoAnswerPanelViewModel.Selected):
                    DeleteAutoAnswerRuleCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(AutoAnswerPanelViewModel.AnyEnabled):
                    DisableAllAutoAnswerRulesCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(AutoAnswerPanelViewModel.AnyDisabled):
                    EnableAllAutoAnswerRulesCommand.NotifyCanExecuteChanged();
                    break;
            }
        };

        ApplyStoredPreferences();
    }

    public AutoAnswerPanelViewModel AutoAnswerPanel { get; }

    // Handle newly displayed frames.
    protected override void OnFramesAppended(
        IReadOnlyList<(SerialFrame Frame, string? Alias)> batch)
    {
        foreach (var (frame, _) in batch)
        {
            if (AutoAnswerPanel.Answer(frame) is not { } answer) continue;

            _ = AnswerAsync(answer);
        }
    }


    // Create an automatic reply rule.
    [RelayCommand]
    private async Task AddAutoAnswerRule()
    {
        if (await Context.DialogService.ShowAutoAnswerRuleDialogAsync(null) is not { } draft) return;

        var result = AutoAnswerPanel.Add(
            draft.Name, draft.TriggerFormat, draft.TriggerText,
            draft.ResponseFormat, draft.ResponseText, draft.Terminator);

        if (result == AutoAnswerSaveResult.Full) ReportAutoAnswerFull();
    }

    // Edit an automatic reply rule.
    [RelayCommand]
    private async Task EditAutoAnswerRule()
    {
        if (AutoAnswerPanel.Selected is not { } row) return;
        if (await Context.DialogService.ShowAutoAnswerRuleDialogAsync(row.Rule) is not { } draft) return;

        AutoAnswerPanel.Update(
            row.Id, draft.Name, draft.TriggerFormat, draft.TriggerText,
            draft.ResponseFormat, draft.ResponseText, draft.Terminator);
    }

    // Delete an automatic reply rule.
    [RelayCommand(CanExecute = nameof(HasAutoAnswerSelection))]
    private void DeleteAutoAnswerRule()
    {
        if (AutoAnswerPanel.Selected is { } row) AutoAnswerPanel.Delete(row.Id);
    }

    private bool HasAutoAnswerSelection => AutoAnswerPanel.Selected is not null;

    // Move a reply rule one place up.
    [RelayCommand]
    private void MoveAutoAnswerRuleUp()
    {
        if (AutoAnswerPanel.Selected is { } row) AutoAnswerPanel.MoveForwardOne(row.Id);
    }

    // Move a reply rule down one slot.
    [RelayCommand]
    private void MoveAutoAnswerRuleDown()
    {
        if (AutoAnswerPanel.Selected is { } row) AutoAnswerPanel.MoveBackOne(row.Id);
    }

    // Move a reply rule to the front.
    [RelayCommand]
    private void MoveAutoAnswerRuleToFront()
    {
        if (AutoAnswerPanel.Selected is { } row) AutoAnswerPanel.MoveToFront(row.Id);
    }

    // Reset the reply trigger count.
    [RelayCommand]
    private void ClearAutoAnswerCount()
    {
        if (AutoAnswerPanel.Selected is { } row) AutoAnswerPanel.ClearCount(row.Id);
    }

    // Disable automatic reply rules.
    [RelayCommand(CanExecute = nameof(AnyAutoAnswerRuleEnabled))]
    private void DisableAllAutoAnswerRules() => AutoAnswerPanel.DisableAll();

    // Enable automatic reply rules.
    [RelayCommand(CanExecute = nameof(AnyAutoAnswerRuleDisabled))]
    private void EnableAllAutoAnswerRules() => AutoAnswerPanel.EnableAll();

    private bool AnyAutoAnswerRuleEnabled => AutoAnswerPanel.AnyEnabled;

    private bool AnyAutoAnswerRuleDisabled => AutoAnswerPanel.AnyDisabled;

    // Report the reply rule limit.
    private void ReportAutoAnswerFull() =>
        Context.SessionLog?.Append(
            DateTimeOffset.Now, LogLevel.Warning, "AutoAnswer",
            Context.Localization[LocKeys.AutoAnswerErrorFull]);

    // Send a matched automatic reply.
    private async Task AnswerAsync(ArmedRule rule)
    {
        var outcome = await WriteToTheWireAsync(
            SendPanel.TargetChannel, rule.Response, asAutoAnswer: true);

        Context.SessionLog?.Append(
            DateTimeOffset.Now,
            outcome == WireWriteOutcome.Sent ? LogLevel.Information : LogLevel.Warning,
            "AutoAnswer",
            outcome switch
            {
                WireWriteOutcome.Sent => $"{Named(rule)}sent {rule.Response.Length} bytes",
                WireWriteOutcome.NotConnected => $"{Named(rule)}matched, but the port is not open",
                _ => $"{Named(rule)}matched, but the write failed"
            });
    }

    // Build a named setting.
    private static string Named(ArmedRule rule) =>
        string.IsNullOrWhiteSpace(rule.Name) ? string.Empty : rule.Name + ": ";

    public SerialPortInfo Port { get; }

    public SerialPortSettings Settings { get; }

    public override SessionKind Kind => SessionKind.Terminal;

    // Describe the recorded batch.
    protected override RecordingBatchInfo DescribeRecordingBatch() =>
        new(SessionKind.Terminal, Port.PortName, null, null, null, Settings);

    // Build the export base name.
    protected override string DescribeExportBaseName(string kind) =>
        $"diserial-{SerialPortInfo.FileNameSegment(Port.PortName)}" +
        $"-{Settings.ShortDescription.Replace(' ', '-')}" +
        $"-{DateTime.Now:yyyyMMdd-HHmmss}";

    public override string Title => LF(LocKeys.SessionTerminalTitle, Port.PortName);

    public override string StatusText => LF(
        LocKeys.SessionTerminalStatus,
        Port.PortName,
        Settings.ShortDescription,
        DescribeState(),
        LogPanel.FrameCount,
        LogPanel.ByteCount);

    public double PanelHeight
    {
        get => Context.Settings.Options.PanelHeight;
        set
        {
            var clamped = AppOptions.ClampPanelHeight((int)Math.Round(value));
            if (clamped == Context.Settings.Options.PanelHeight) return;

            Context.Settings.Options = Context.Settings.Options with { PanelHeight = clamped };
            OnPropertyChanged();
        }
    }
}
