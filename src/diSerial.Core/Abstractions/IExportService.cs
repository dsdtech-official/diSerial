using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

public interface IExportService
{
    IReadOnlyList<ExportFormat> SupportedFormats { get; }

    // Export the selected frame data.
    Task ExportAsync(
        IEnumerable<SerialFrame> frames,
        string filePath,
        ExportOptions options,
        CancellationToken cancellationToken = default);
}

public enum ExportFormat
{
    Tsv,
    Csv
}

// Hold export formatting options.
public sealed record ExportOptions
{
    public ExportFormat Format { get; init; } = ExportFormat.Tsv;

    public DisplayFormat DisplayFormat { get; init; } = DisplayFormat.HexAndAscii;

    public TimestampMode TimestampMode { get; init; } = TimestampMode.Absolute;

    public bool IncludeChannelColumn { get; init; }

    public bool IncludeDeltaColumn { get; init; }

    public string? ChannelPortA { get; init; }

    public string? ChannelPortB { get; init; }

    public string? ChannelAliasA { get; init; }

    public string? ChannelAliasB { get; init; }
}
