using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

// Create session recorders.
public sealed class SessionRecorderFactory(IAppPaths paths, ILoggerFactory loggerFactory)
    : ISessionRecorderFactory
{
    // Create a SQLite recorder.
    public ISessionRecorder Create() =>
        new SqliteSessionRecorder(paths.RecordingDatabasePath, loggerFactory.CreateLogger("Recording"));
}
