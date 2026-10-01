using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

// Manage the frame timeline.
public sealed partial class LogPanelViewModel : ViewModelBase
{
    private readonly IFrameFormatter _formatter;

    private const int DefaultMaxFrames = 500;

    public int MaxFrames { get; set; } = DefaultMaxFrames;

    // Initialize the frame log panel.
    public LogPanelViewModel(IFrameFormatter formatter, IEnumChoiceProvider enumChoices)
    {
        _formatter = formatter;
        DisplayFormatChoices = enumChoices.GetChoices<DisplayFormat>();
        TimestampModeChoices = enumChoices.GetChoices<TimestampMode>();
    }

    public ObservableCollection<FrameViewModel> Frames { get; } = [];

    public IReadOnlyList<LocalizedEnumItem> DisplayFormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> TimestampModeChoices { get; }

    [ObservableProperty]
    private DisplayFormat _displayFormat = DisplayFormat.HexAndAscii;

    [ObservableProperty]
    private TimestampMode _timestampMode = TimestampMode.Absolute;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _showChannelColumn;

    [ObservableProperty]
    private bool _showDeltaColumn;

    [ObservableProperty]
    private bool _showSentData = true;

    [ObservableProperty]
    private int _frameCount;

    [ObservableProperty]
    private long _byteCount;

    // Append a captured frame.
    public void Append(SerialFrame frame, string? channelLabel = null)
    {
        AppendCore(frame, channelLabel);
        AddToCounters(1, frame.Length);
        Trim();
    }

    // Append a batch of frames.
    public void AppendRange(IReadOnlyList<(SerialFrame Frame, string? Label)> batch)
    {
        if (batch.Count == 0) return;

        long bytes = 0;

        foreach (var (frame, label) in batch)
        {
            AppendCore(frame, label);
            bytes += frame.Length;
        }

        AddToCounters(batch.Count, bytes);
        Trim();
    }

    // Refresh labels for a channel.
    public void RelabelChannel(ChannelId channel, string? channelLabel)
    {
        foreach (var frame in Frames)
        {
            if (frame.Channel == channel) frame.Relabel(channelLabel);
        }
    }

    // Append a frame and its counters.
    private void AppendCore(SerialFrame frame, string? channelLabel)
    {
        if (IsPaused) return;

        Frames.Add(new FrameViewModel(frame, _formatter, DisplayFormat, TimestampMode, channelLabel, HexBytesPerLine));
    }

    // Update frame and byte counters.
    private void AddToCounters(int frames, long bytes)
    {
        if (frames == 0) return;

        FrameCount += frames;
        ByteCount += bytes;
    }

    // Enforce the frame display limit.
    private void Trim()
    {
        while (Frames.Count > MaxFrames)
        {
            Frames.RemoveAt(0);
        }
    }

    // Clear displayed frames.
    [RelayCommand]
    private void Clear()
    {
        Frames.Clear();
        FrameCount = 0;
        ByteCount = 0;
    }

    // Toggle the display pause state.
    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    public event EventHandler<string>? FrameDataRequested;

    // Request the full frame display.
    [RelayCommand]
    private void ShowFrameData(FrameViewModel? frame)
    {
        if (frame is null || frame.DataText.Length == 0) return;

        FrameDataRequested?.Invoke(this, frame.DataText);
    }

    // Refresh frame data formatting.
    partial void OnDisplayFormatChanged(DisplayFormat value) => RefreshAll();

    // Refresh frame timestamp text.
    partial void OnTimestampModeChanged(TimestampMode value) => RefreshAll();

    // Refresh all displayed frames.
    public void RefreshAll()
    {
        foreach (var frame in Frames)
        {
            frame.Refresh(DisplayFormat, TimestampMode, HexBytesPerLine);
        }
    }

    public int HexBytesPerLine { get; private set; } = 16;

    // Set the data column text width.
    public void SetDataColumnCharacters(int characters)
    {
        var fit = (characters - 1) / 4;

        var quantised = Math.Max(8, fit / 8 * 8);

        if (quantised == HexBytesPerLine) return;

        HexBytesPerLine = quantised;
        OnPropertyChanged(nameof(HexBytesPerLine));
        RefreshAll();
    }
}
