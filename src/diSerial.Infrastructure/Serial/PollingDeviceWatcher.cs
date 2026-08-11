using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using DiSerial.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DiSerial.Infrastructure.Serial;

/// <summary>
/// 基于定时轮询的端口插拔监视（C-03a）。
///
/// 每隔固定周期枚举一次端口，与上一次结果比对差集，有增删就触发 <see cref="PortsChanged"/>。
/// **消费者是新建会话对话框** —— 对话框开着时插拔串口，端口下拉即时跟着变。
///
/// 轮询周期是准确性与开销的折中：Windows 上一次枚举含 WMI 查询约 250ms，
/// 过密会持续占用 CPU；过疏则下拉刷新变迟钝。
///
/// ⚠️ <b>不再做「双通道设备识别」</b>（2026-07-29 移除，原 M-02）。
/// 原先「同一次轮询新增恰好两个端口 → 判定为双通道设备接入」，
/// UI 据此弹插入提示。移除理由见 <see cref="IDeviceWatcher"/> 的注释。
/// 连带移除的还有 <c>IDeviceSimulator</c>（它唯一的用途就是伪造那个事件）。
/// </summary>
public sealed class PollingDeviceWatcher(
    IPortEnumerator portEnumerator,
    ILogger? logger = null,
    TimeSpan? pollInterval = null)
    : IDeviceWatcher
{
    /// <summary>
    /// 可为 null：单元测试直接 new，不必搭日志管线。
    ///
    /// ⚠️ 类型是 <c>ILogger</c> 而不是 <c>ILogger&lt;PollingDeviceWatcher&gt;</c> ——
    /// 后者的类别名会是 <c>DiSerial.Infrastructure.Serial.PollingDeviceWatcher</c>，
    /// 打在每一行日志上，与 <c>Serial.COM1</c> / <c>Session</c> / <c>Diagnostics.*</c>
    /// 这套短名格格不入。组合根按 <c>Devices</c> 建，见 Infrastructure 的注册处。
    /// </summary>
    private readonly ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Production always uses <see cref="DefaultPollInterval"/>; the parameter exists so
    /// the P1-46 guardrails can drive several rounds without sleeping for seconds.
    ///
    /// ⚠️ It is a <b>seam, not a setting</b> -- nothing in the app passes it, and the
    /// composition root must keep leaving it alone. Those guardrails are only possible
    /// because the loop can be made to tick fast, and "the loop must not die quietly"
    /// is exactly the kind of convention that has to become a test rather than a habit
    /// (03-conventions, section 1).
    /// </summary>
    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;

    private readonly Lock _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IReadOnlyList<SerialPortInfo> _lastSnapshot = [];

    /// <summary>
    /// ⚠️ <b>Guarded by <see cref="_sync"/>. Claims the start before the first await</b>
    /// (P2-81). <see cref="IsRunning"/> cannot do this job: it must stay false until the loop
    /// actually exists, or a failed start would leave the flag lying -- which is the very
    /// thing P1-46 was about.
    /// </summary>
    private bool _starting;

    private volatile bool _isRunning;

    /// <summary>
    /// ⚠️ <b>volatile because P1-46 gave the poll loop itself a reason to write this.</b>
    /// Until then only <see cref="StartAsync"/> / <see cref="StopAsync"/> touched it, both
    /// on the caller's thread; now the loop clears it on the way out after a fault.
    /// A status bit written on one thread and read on another has to say so in the type
    /// rather than in a comment somebody has to find.
    /// </summary>
    public bool IsRunning { get => _isRunning; private set => _isRunning = value; }

    public event EventHandler<PortsChangedEventArgs>? PortsChanged;

    /// <summary>
    /// ⛔ <b>P2-81: the guard and the flag are separated by an await, so the claim has to be
    /// staked under the lock.</b> On Windows that await contains a WMI query -- measured at
    /// roughly 250ms, not a microsecond window -- and two calls landing inside it would both
    /// pass a bare <c>if (IsRunning)</c>, each build a loop, and each overwrite <c>_cts</c> /
    /// <c>_loop</c>. The first source would then never be disposed (the exact shape of P2-65)
    /// and its loop could outlive <c>StopAsync</c> entirely.
    ///
    /// <para>⚠️ <b>Nothing calls this concurrently today</b> -- App.axaml.cs starts it once.
    /// This is structure against a future edit, like the P2-64 isolation below it: the
    /// obvious next feature here is a "rescan ports" command or a restart-after-failure path,
    /// and whoever writes it will not read this comment first.</para>
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isRunning || _starting) return;
            _starting = true;
        }

        try
        {
            // 先取一次基线，否则启动瞬间已存在的端口会被误报为「刚插入」。
            //
            // ⛔ P2-80: this used to sit outside any try. SerialPort.GetPortNames() casts each
            // registry value with (string), so a non-REG_SZ value under SERIALCOMM throws
            // InvalidCastException, and a value deleted between GetValueNames() and GetValue()
            // yields a null element that NumericAwareKey then dereferences. Either one escaped
            // StartAsync before the loop existed, and the App layer discarded the task, so
            // hot-plug detection was gone for the process with nothing written anywhere.
            //
            // ⭐ The scope of the failure has to match the scope of the feature (P1-46). One bad
            // baseline costs the baseline, not the watch.
            var baselineKnown = true;
            try
            {
                _lastSnapshot = await portEnumerator.GetPortsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the caller asking to stop, not a fault: starting a loop it
                // just asked us not to start would be the wrong reading of it.
                throw;
            }
            catch (Exception e)
            {
                baselineKnown = false;
                _lastSnapshot = [];
                DeviceWatcherLog.BaselineFailed(_logger, e, e.GetType().Name);
            }

            // 基线要留痕：第一条 PortsChanged 之前发生了什么，只有这条能说明
            // （03-conventions 8.4.5「关键动作要在开始也留痕」）。
            if (baselineKnown)
            {
                DeviceWatcherLog.Started(
                    _logger, _lastSnapshot.Count, DeviceWatcherLog.Describe(_lastSnapshot));
            }

            // ⚠️ P2-81: locals, not fields. The lambda used to close over `_cts`, so what the
            // loop actually received depended on who wrote the field last -- two loops could
            // share one token, or one could be left holding a source nobody cancels.
            var cts = new CancellationTokenSource();
            var loop = Task.Run(() => PollLoopAsync(cts.Token, baselineKnown), CancellationToken.None);

            lock (_sync)
            {
                _cts = cts;
                _loop = loop;
            }

            IsRunning = true;
        }
        finally
        {
            lock (_sync) _starting = false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not { } cts) return;

        await cts.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        cts.Dispose();
        _cts = null;
        _loop = null;
        IsRunning = false;

        DeviceWatcherLog.Stopped(_logger);
    }

    // ⚠️ 原先这里有 SimulatedPortA / SimulatedPortB（"SIM-A" / "SIM-B"）两个常量，
    // 2026-07-30 连同 SimulatedPortEnumerator 与 StubCaptureSession 一并删除。
    //
    // 那两个端口**从不真正打开**，唯一用途是让人在无硬件时建一个走桩的监听会话；
    // 而桩已被 MonitorCaptureSession 替掉（P0-1），于是选它们只会走真实打开路径并失败。
    //
    // 「合成端口的名字不得与真实端口混淆」这条判据**没有丢** —— 它转移到了回放端口上，
    // 由 ReplayPortListingTests 守着（REPLAY-* 有完全相同的要求与理由）。
    // 无硬件时要看监听会话，用两个 REPLAY-* 端口，走的是真实采集链路，比桩更可信。

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        PortsChanged = null;
    }

    /// <param name="baselineKnown">
    /// ⭐ <b>False when the baseline enumeration failed</b> (P2-80). The first successful poll
    /// then <b>adopts</b> its result instead of diffing against an empty snapshot -- otherwise
    /// every port already plugged in would be announced as freshly inserted, which is exactly
    /// the lie the baseline exists to prevent.
    /// </param>
    private async Task PollLoopAsync(CancellationToken token, bool baselineKnown = true)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        var needsBaseline = !baselineKnown;

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                IReadOnlyList<SerialPortInfo> current;
                try
                {
                    current = await portEnumerator.GetPortsAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    // One failed enumeration (a WMI hiccup and the like) skips this round
                    // and nothing more. The next tick retries 1.5s later, so a transient
                    // fault costs one stale round; letting it take the watcher down costs
                    // hot-plug detection for the rest of the process.
                    //
                    // P1-46: this used to be a whitelist (IOException /
                    // UnauthorizedAccessException). Anything outside it -- a COMException
                    // escaping WMI, say -- fell through to the loop's outer catch and
                    // killed it. The whitelist was the bug, not the handling.
                    //
                    // ⚠️ Debug, not Warning: this loop runs every 1.5s, so a persistent
                    // failure fires ~40 times a minute and a Warning would drown out the
                    // real problems (03-conventions 8.0 / 8.2 (2); 01-spec 4.7 shared
                    // rule 1: anything that can repeat goes to Debug). Invisible at the
                    // default info volume -- turn logLevel up to debug and it is all
                    // there. Contrast DeviceWatcherLog.Faulted, which fires once and is
                    // therefore Error.
                    _logger.LogDebug(e, "Port enumeration failed for this poll; skipping the round.");
                    continue;
                }

                // ⭐ P2-80: the start had no baseline, so this round supplies one and reports
                // nothing. "Nothing is known to have changed" is the honest statement when
                // nothing was known -- see DeviceWatcherLog.BaselineRecovered.
                if (needsBaseline)
                {
                    needsBaseline = false;
                    lock (_sync) _lastSnapshot = current;

                    DeviceWatcherLog.BaselineRecovered(
                        _logger, current.Count, DeviceWatcherLog.Describe(current));
                    continue;
                }

                Compare(current, out var added, out var removed);
                if (added.Count == 0 && removed.Count == 0)
                {
                    // 无变化的轮次压在 Trace —— 1.5 秒一轮，40 行/分钟。
                    // 它回答的是「轮询到底还在跑吗」，那是别处拿不到的证据。
                    DeviceWatcherLog.PolledNoChange(_logger, current.Count);
                    continue;
                }

                // 完整列表 + 增减 diff（03-conventions 8.4 诊断契约，服务 Q-7）。
                // 三样都记：diff 说明变了什么，current 说明变完之后是什么样 ——
                // 只有 diff 时无法判断下拉里最终该有几项（8.4.5「记做了什么」）。
                DeviceWatcherLog.PortsChanged(
                    _logger, added.Count, removed.Count, current.Count,
                    DeviceWatcherLog.Describe(added),
                    DeviceWatcherLog.Describe(removed),
                    DeviceWatcherLog.Describe(current));

                // ⭐ P2-64: a throwing subscriber must not take hot-plug detection down with it.
                //
                // ⛔ Until 2026-08-05 this call sat bare, so any exception out of a handler fell
                // through to the loop's outer catch, which logs and clears IsRunning -- and there
                // is no path that ever starts the loop again. One bad handler therefore cost the
                // port dropdown for the rest of the process, on a tool whose whole job is to
                // notice hardware appearing and disappearing.
                //
                // ⚠️ The subscriber is our own code, so this is defence against a future edit
                // rather than a bug being observed today. That is exactly why it is structure and
                // not a comment asking the next person to be careful: the failure it prevents is
                // silent, and the next PortsChanged handler will not be written by whoever read
                // the outer catch.
                //
                // ⛔ NOT swallowed: the log is Error and fires once per occurrence, and the
                // snapshot has already been updated, so the next tick reports the delta from
                // here rather than replaying this one (01-spec 4.7 shared rule 1).
                try
                {
                    PortsChanged?.Invoke(this, new PortsChangedEventArgs(added, removed, current));
                }
                catch (Exception e)
                {
                    DeviceWatcherLog.SubscriberFailed(_logger, e, e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            // P1-46: whatever reaches here has already ended the loop -- a throwing
            // PortsChanged subscriber, a bug in Compare, the timer itself. What made it
            // a defect rather than an accident is what used to happen next: nothing.
            // No log, and IsRunning went on reporting true, so the port dropdown quietly
            // stopped refreshing with no signal anywhere that it had.
            //
            // ⭐ Clearing the flag is the actual fix; the log line is how anyone finds
            // out why. A status bit that outlives the thing it describes is the shape
            // this project calls "the tool is lying" (00-STATUS, tier 1) -- worse than a
            // crash, because the user cannot tell they are being misinformed.
            DeviceWatcherLog.Faulted(_logger, e, e.GetType().Name);
            IsRunning = false;
        }
    }

    private void Compare(
        IReadOnlyList<SerialPortInfo> current,
        out IReadOnlyList<SerialPortInfo> added,
        out IReadOnlyList<SerialPortInfo> removed)
    {
        lock (_sync)
        {
            var previousNames = _lastSnapshot
                .Select(p => p.PortName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var currentNames = current
                .Select(p => p.PortName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            added = current.Where(p => !previousNames.Contains(p.PortName)).ToArray();
            removed = _lastSnapshot.Where(p => !currentNames.Contains(p.PortName)).ToArray();

            _lastSnapshot = current;
        }
    }
}
