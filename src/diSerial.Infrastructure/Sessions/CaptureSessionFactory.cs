using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Sessions;

/// <summary>
/// 采集会话工厂。**两种会话都是真实实现**，串口字节流到
/// <see cref="SerialFrame"/> 的全链路：<c>ISerialPort → IFrameSplitter → SerialFrame</c>。
///
/// <list type="bullet">
///   <item><see cref="CreateTerminal"/> —— 单端口，见 <see cref="TerminalCaptureSession"/></item>
///   <item><see cref="CreateMonitor"/> —— 双端口旁路监听，见 <see cref="MonitorCaptureSession"/></item>
/// </list>
///
/// 归并设计（双通道如何合到单一时间轴、Δms 的语义与计算位置）
/// 已于 2026-07-30 定完，见 <c>docs/02-architecture.md</c> 第八节。
///
/// ⚠️ <b>本注释 2026-07-31 重写（P2-16 a）。</b>原文写着「监听会话 —— 仍为桩实现，
/// 本轮不在范围内」，并把上面那两个问题列为「端到端跑通后才能定，故本轮刻意不预先设计」——
/// <b>而真实实现 2026-07-30 就落在它正下方</b>。四处同类过期描述里这一处最误导：
/// <b>代码注释比文档更容易被当成当前事实</b>，下一个人极可能照着它以为监听还是桩。
/// </summary>
public sealed class CaptureSessionFactory(
    ISerialPortFactory portFactory,
    IFrameSplitterFactory splitterFactory,
    IMonotonicClock clock,
    ILoggerFactory loggerFactory) : ICaptureSessionFactory
{
    /// <summary>
    /// ✅ <b>Both session types now get a logger</b> — the asymmetry this comment used to
    /// document ended with P1-11 on 2026-08-05.
    ///
    /// <para>⭐ <b>What that entry decided, since the seam was left open for it</b>: a session
    /// records its <i>state transitions</i> (<c>SessionLog.StateTransition</c>, 14xx range), and
    /// that applies to every session type, so the terminal session needed a logger after all.
    /// The earlier refusal was specifically about injecting one with <i>nothing to log</i>;
    /// that condition no longer holds.</para>
    ///
    /// <para>⚠️ Category name <c>Session</c>, same as <see cref="CreateMonitor"/> and
    /// <c>SessionViewModel</c> — session-level facts stay one filterable dimension.</para>
    /// </summary>
    public ICaptureSession CreateTerminal(string portName, SerialPortSettings settings)
    {
        var port = portFactory.Create(portName, settings);
        var splitter = splitterFactory.Create(settings);
        return new TerminalCaptureSession(
            port, splitter, clock, loggerFactory.CreateLogger("Session"));
    }

    /// <summary>
    /// 双串口监听（P0-1）。**每通道一个端口、一个 splitter** ——
    /// 通道归属由 <see cref="MonitorCaptureSession"/> 在组装帧时打上，
    /// <c>IFrameSplitter</c> 不需要知道自己属于哪一路。
    ///
    /// 类别名用 <c>Session</c>，与 <c>SessionViewModel</c> 的 logger 同名 ——
    /// 会话这一层的事归在一个可过滤维度下。
    /// </summary>
    public ICaptureSession CreateMonitor(SerialChannelPair pair, SerialPortSettings settings)
        => new MonitorCaptureSession(
            portFactory.Create(pair.ChannelA.PortName, settings),
            portFactory.Create(pair.ChannelB.PortName, settings),
            splitterFactory.Create(settings),
            splitterFactory.Create(settings),
            clock,
            loggerFactory.CreateLogger("Session"));
}
