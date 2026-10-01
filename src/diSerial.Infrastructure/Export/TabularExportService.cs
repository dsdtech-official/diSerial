using System.Globalization;
using System.Text;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.Infrastructure.Export;

// Export frames as tabular data.
public sealed class TabularExportService(IFrameFormatter formatter) : IExportService
{
    private const char TabSeparator = '\t';
    private const char CommaSeparator = ',';

    private static readonly char[] CsvSpecials = [CommaSeparator, '"', '\r', '\n'];

    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    public IReadOnlyList<ExportFormat> SupportedFormats { get; } =
        [ExportFormat.Tsv, ExportFormat.Csv];

    // Select the export delimiter.
    private static char SeparatorFor(ExportFormat format) =>
        format == ExportFormat.Csv ? CommaSeparator : TabSeparator;

    // Escape a delimited export cell.
    private static string Encode(string text, ExportFormat format) => format == ExportFormat.Csv
        ? (text.IndexOfAny(CsvSpecials) >= 0 ? '"' + text.Replace("\"", "\"\"") + '"' : text)
        : Flatten(text);

    // Export the selected frame data.
    public async Task ExportAsync(
        IEnumerable<SerialFrame> frames,
        string filePath,
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(false));

        writer.NewLine = "\r\n";

        await writer.WriteLineAsync(BuildHeader(options));

        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(BuildRow(frame, options));
        }
    }

    // Build the export header row.
    private static string BuildHeader(ExportOptions options)
    {
        var columns = new List<string> { "Seq", "Time" };
        if (options.IncludeDeltaColumn) columns.Add("DeltaMs");
        if (options.IncludeChannelColumn) { columns.Add("Port"); columns.Add("Alias"); }
        columns.Add("Direction");

        columns.Add("Data");
        columns.Add("DataAscii");
        return string.Join(SeparatorFor(options.Format), columns);
    }

    // Resolve a frame port name.
    private static string PortOf(ChannelId channel, ExportOptions options) => channel switch
    {
        ChannelId.A => options.ChannelPortA ?? string.Empty,
        ChannelId.B => options.ChannelPortB ?? string.Empty,
        _ => string.Empty
    };

    // Resolve a frame channel alias.
    private static string AliasOf(ChannelId channel, ExportOptions options) => channel switch
    {
        ChannelId.A => options.ChannelAliasA ?? string.Empty,
        ChannelId.B => options.ChannelAliasB ?? string.Empty,
        _ => string.Empty
    };

    // Build a frame export row.
    private string BuildRow(SerialFrame frame, ExportOptions options)
    {
        var cells = new List<string>
        {
            frame.Sequence.ToString(Fmt),
            formatter.FormatTimestamp(frame, options.TimestampMode)
        };

        if (options.IncludeDeltaColumn)
        {
            cells.Add(frame.Delta.HasValue
                ? frame.Delta.Value.TotalMilliseconds.ToString("F1", Fmt)
                : string.Empty);
        }

        if (options.IncludeChannelColumn)
        {
            cells.Add(Encode(PortOf(frame.Channel, options), options.Format));
            cells.Add(Encode(AliasOf(frame.Channel, options), options.Format));
        }

        cells.Add(frame.Direction.ToString());

        cells.Add(Encode(formatter.FormatData(frame.Data, DisplayFormat.Hex), options.Format));

        cells.Add(Encode(
            options.DisplayFormat == DisplayFormat.Hex
                ? string.Empty
                : formatter.FormatData(frame.Data, DisplayFormat.Ascii),
            options.Format));

        return string.Join(SeparatorFor(options.Format), cells);
    }

    // Flatten decoded field values.
    private static string Flatten(string text) => text
        .Replace("\r\n", " ")
        .Replace('\n', ' ')
        .Replace('\r', ' ')
        .Replace(TabSeparator, ' ');
}
