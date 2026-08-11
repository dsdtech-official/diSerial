using DiSerial.Core.Abstractions;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Recording;

/// <summary>
/// ISessionRecorderFactory 的落地实现，产出 <see cref="SqliteSessionRecorder"/>。
///
/// 依赖（落库路径、logger）在此按构造注入取得，与 <c>CaptureSessionFactory</c>
/// 拿 ISerialPortFactory / IFrameSplitterFactory / IMonotonicClock 的方式相同。
///
/// ⚠️ <b>产出物不进容器的释放列表</b> —— 工厂直接 new，生命周期完全由调用方掌握。
/// 理由见 <see cref="ISessionRecorderFactory"/> 的接口注释（那是 P0-5 修掉的泄漏）。
/// </summary>
public sealed class SessionRecorderFactory(IAppPaths paths, ILoggerFactory loggerFactory)
    : ISessionRecorderFactory
{
    public ISessionRecorder Create() =>
        new SqliteSessionRecorder(paths.RecordingDatabasePath, loggerFactory.CreateLogger("Recording"));
}
