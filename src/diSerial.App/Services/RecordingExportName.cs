using DiSerial.Core.Abstractions;

namespace DiSerial.App.Services;

// Build recording export names.
public static class RecordingExportName
{
    // Build a batch export filename.
    public static string ForBatch(RecordingBatchSummary summary) =>
        $"diserial-{summary.PortLabel}-{summary.SettingsLabel}-{summary.StartedAt.LocalDateTime:yyyyMMdd-HHmmss}";
}
