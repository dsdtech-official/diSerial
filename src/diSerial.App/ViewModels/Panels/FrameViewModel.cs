using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Panels;

// Render one captured frame.
public sealed class FrameViewModel : ViewModelBase
{
    private readonly IFrameFormatter _formatter;

    // Initialize frame display data.
    public FrameViewModel(SerialFrame frame, IFrameFormatter formatter,
        DisplayFormat displayFormat, TimestampMode timestampMode, string? channelLabel = null,
        int hexBytesPerLine = 16)
    {
        Frame = frame;
        _formatter = formatter;

        ChannelLabel = channelLabel;

        Refresh(displayFormat, timestampMode, hexBytesPerLine);
    }

    public SerialFrame Frame { get; }

    public long Sequence => Frame.Sequence;

    public ChannelId Channel => Frame.Channel;

    public bool IsError => Frame.Flags != FrameFlags.None;

    public bool IsTx => Frame.Direction == FrameDirection.Tx;

    public FrameAccent Accent => Frame.Channel switch
    {
        ChannelId.A => FrameAccent.ChannelA,
        ChannelId.B => FrameAccent.ChannelB,
        _ => IsTx ? FrameAccent.Tx : FrameAccent.Rx
    };

    public string TimestampText { get; private set; } = string.Empty;

    public string DeltaText { get; private set; } = string.Empty;

    public string ChannelText { get; private set; } = string.Empty;

    public string DataText { get; private set; } = string.Empty;

    public string? DecodedSummary { get; private set; }

    public string? ChannelLabel { get; private set; }

    // Refresh the frame channel label.
    public void Relabel(string? channelLabel)
    {
        if (string.Equals(ChannelLabel, channelLabel, StringComparison.Ordinal)) return;

        ChannelLabel = channelLabel;
        ChannelText = BuildChannelText();
        OnPropertyChanged(nameof(ChannelText));
    }

    // Refresh the frame display text.
    public void Refresh(DisplayFormat displayFormat, TimestampMode timestampMode,
        int hexBytesPerLine = 16)
    {
        TimestampText = _formatter.FormatTimestamp(Frame, timestampMode);
        DecodedSummary = _formatter.FormatDecodedSummary(Frame);
        DeltaText = Frame.Delta is { } d ? DurationText.Milliseconds(d) : "—";
        DataText = _formatter.FormatData(Frame.Data, displayFormat, hexBytesPerLine);
        ChannelText = BuildChannelText();

        OnPropertyChanged(nameof(TimestampText));
        OnPropertyChanged(nameof(DecodedSummary));
        OnPropertyChanged(nameof(DeltaText));
        OnPropertyChanged(nameof(DataText));
        OnPropertyChanged(nameof(ChannelText));
    }

    // Build the frame channel label.
    private string BuildChannelText() => _formatter.FormatChannelText(Frame, ChannelLabel);
}
