using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Manage export options.
public sealed partial class ExportDialogViewModel : LocalizedViewModelBase
{
    private readonly IDialogService _dialogs;

    private readonly ExportChannels _channels;

    private readonly string? _preferredDirectory;

    // Initialize export dialog state.
    public ExportDialogViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        IDialogService dialogs,
        ExportDialogSeed seed)
        : base(localization)
    {
        _dialogs = dialogs;

        FormatChoices = enumChoices.GetChoices<ExportFormat>();
        DisplayFormatChoices = enumChoices.GetChoices<DisplayFormat>();
        TimestampModeChoices = enumChoices.GetChoices<TimestampMode>();

        Format = seed.Format;
        DisplayFormat = seed.DisplayFormat;
        TimestampMode = seed.TimestampMode;
        IncludeDeltaColumn = seed.IncludeDeltaColumn;
        SuggestedBaseName = seed.SuggestedBaseName;
        _channels = seed.Channels;
        _preferredDirectory = seed.PreferredDirectory;

        HasChannelIdentity = _channels.PortA is not null || _channels.PortB is not null;
        IncludeChannelColumn = seed.IncludeChannelColumn && HasChannelIdentity;

        FilePath = BuildDefaultPath();
    }

    public IReadOnlyList<LocalizedEnumItem> FormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> DisplayFormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> TimestampModeChoices { get; }

    public string SuggestedBaseName { get; }

    public bool HasChannelIdentity { get; }

    [ObservableProperty]
    private ExportFormat _format = ExportFormat.Tsv;

    [ObservableProperty]
    private DisplayFormat _displayFormat;

    [ObservableProperty]
    private TimestampMode _timestampMode;

    [ObservableProperty]
    private bool _includeChannelColumn;

    [ObservableProperty]
    private bool _includeDeltaColumn;

    [ObservableProperty]
    private string _filePath = string.Empty;

    public ExportRequest? Result { get; private set; }

    public event EventHandler? CloseRequested;

    // Update the export extension.
    partial void OnFormatChanged(ExportFormat value) => FilePath = BuildDefaultPath();

    // Choose an export destination.
    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _dialogs.ShowSaveFileDialogAsync(
            Path.GetFileName(FilePath), Extension);

        if (!string.IsNullOrWhiteSpace(picked)) FilePath = picked;
    }

    // Accept the dialog values.
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new ExportRequest(FilePath, new ExportOptions
        {
            Format = Format,
            DisplayFormat = DisplayFormat,
            TimestampMode = TimestampMode,
            IncludeChannelColumn = IncludeChannelColumn,
            IncludeDeltaColumn = IncludeDeltaColumn,
            ChannelPortA = _channels.PortA,
            ChannelAliasA = _channels.AliasA,
            ChannelPortB = _channels.PortB,
            ChannelAliasB = _channels.AliasB
        });
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Check whether values are valid.
    private bool CanConfirm() => !string.IsNullOrWhiteSpace(FilePath);

    // Cancel the current dialog.
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string Extension => Format == ExportFormat.Csv ? ".csv" : ".tsv";

    // Build the default export path.
    private string BuildDefaultPath()
    {
        var directory = !string.IsNullOrWhiteSpace(FilePath)
            ? Path.GetDirectoryName(FilePath) ?? string.Empty
            : !string.IsNullOrWhiteSpace(_preferredDirectory)
                ? _preferredDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return Path.Combine(directory, SuggestedBaseName + Extension);
    }

    // Refresh export validation.
    partial void OnFilePathChanged(string value) => ConfirmCommand.NotifyCanExecuteChanged();
}

// Supply initial export options.
public sealed record ExportDialogSeed(
    string SuggestedBaseName,
    DisplayFormat DisplayFormat,
    TimestampMode TimestampMode,
    bool IncludeChannelColumn,
    bool IncludeDeltaColumn,
    ExportChannels Channels = default,
    string? PreferredDirectory = null,
    ExportFormat Format = ExportFormat.Tsv);

public readonly record struct ExportChannels(
    string? PortA = null,
    string? AliasA = null,
    string? PortB = null,
    string? AliasB = null);

// Describe a data export request.
public sealed record ExportRequest(string FilePath, ExportOptions Options);
