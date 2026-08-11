using DiSerial.Core.Models;

namespace DiSerial.Core.Abstractions;

/// <summary>
/// 会话记录（C-09）。
///
/// <b>存的是原始字节，不是渲染结果</b> —— 规格见 docs/01-spec.md 4.10，
/// 机制见 docs/02-architecture.md 第十二节。
///
/// ⚠️ <b>「开始记录」不需要文件路径</b>：记录直接入库，路径只在**停止后导出**时才由用户选。
/// 这样按下「记录」的成本就是一次点击 —— 出问题那一刻用户想要的正是这个。
///
/// ⚠️ <b>暂停不影响记录</b>：暂停是显示侧的开关（01-spec 4.2a），记录是采集侧的。
/// <b>不要把 <c>IsPaused</c> 接进本接口的任何调用路径</b> ——
/// 否则用户会拿到一份中间缺一段的记录而不自知。
/// </summary>
public interface ISessionRecorder : IAsyncDisposable
{
    bool IsRecording { get; }

    /// <summary>当前批次的主键；未在记录时为 null。</summary>
    long? CurrentBatchId { get; }

    /// <summary>本批次已落库的帧数。</summary>
    long FramesWritten { get; }

    /// <summary>本批次已落库的字节数。</summary>
    long BytesWritten { get; }

    /// <summary>
    /// 写库失败（磁盘满、数据库损坏、路径失效）。
    ///
    /// ⚠️ <b>调用方必须据此停止记录并提示用户</b>（01-spec 4.7 的第 6 条路径）。
    /// 不处理的后果很具体：按钮上写着「停止记录」，而实际早就没在记 ——
    /// 用户放心去复现问题，回来发现记录是空的。
    ///
    /// 刻意做成事件而不是让 <see cref="WriteAsync"/> 抛：写盘在独立任务上，
    /// 失败时采集线程早已走远，抛出去没有人接得住。
    /// </summary>
    event EventHandler<RecordingFailedEventArgs>? Failed;

    /// <summary>开始一个新批次，返回批次主键。</summary>
    Task<long> StartAsync(RecordingBatchInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记一帧。
    ///
    /// ⚠️ <b>由采集线程调用，必须立即返回</b> —— 实现只入队，真正的落盘在独立任务上。
    /// 采集线程正是时间戳打点的地方（03-conventions 7.1），拖慢它等于污染核心卖点。
    /// </summary>
    Task WriteAsync(SerialFrame frame, CancellationToken cancellationToken = default);

    /// <summary>结束当前批次，冲干净缓冲。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个批次的元信息。**导出时不写进文件**（那会破坏纯表格），
/// 关键几项由默认文件名带出去，完整信息留在库里供 V1.1 查询。
/// </summary>
/// <param name="SessionKind">终端 / 监听。</param>
/// <param name="PortA">终端会话的唯一端口，或监听会话的通道 A。</param>
/// <param name="PortB">监听会话的通道 B；终端会话为 null。</param>
public sealed record RecordingBatchInfo(
    SessionKind SessionKind,
    string PortA,
    string? PortB,
    string? AliasA,
    string? AliasB,
    SerialPortSettings Settings);

public sealed class RecordingFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>
/// 记录器工厂 —— 每个会话一个独立实例，由调用方负责释放。
///
/// ⚠️ <b>刻意不用 <c>Func&lt;ISessionRecorder&gt;</c> + Transient 注册。</b>
/// 那种写法里委托捕获的是<b>根容器</b>，而 MS.DI 会把每个从根解析出来的
/// Transient <see cref="IAsyncDisposable"/> 登记进根容器的释放列表，
/// 直到应用退出才清空 —— 调用方释放了也甩不掉，退出时还会被再释放一次。
/// <c>ValidateScopes</c> 抓不到这个模式。
///
/// 工厂直接 new，容器不跟踪，生命周期完全由调用方掌握 ——
/// 与 <see cref="ICaptureSessionFactory"/> 产出 <see cref="ICaptureSession"/> 的方式一致。
/// 两者在 <c>SessionViewModel.DisposeAsync</c> 里是相邻两行释放的，做法必须相同。
/// </summary>
public interface ISessionRecorderFactory
{
    ISessionRecorder Create();
}
