namespace DiSerial.Core.Models;

/// <summary>
/// Classifies an exception thrown by the port layer into a <see cref="SerialErrorKind"/>.
///
/// <para><b>Why this sits in Core.</b> Infrastructure needs it inside the read loop (to report a
/// fault alongside the event) and App needs it in the ViewModel catches (where connect and send
/// failures are caught) -- and App does not reference Infrastructure (composition root aside),
/// so anything both sides share can only live here.</para>
///
/// <para>It depends on BCL exception types only and <b>does not reference System.IO.Ports</b>,
/// so the platform-library ban in ArchitectureTests is unaffected.</para>
///
/// <para>⛔ <b>The exception types below are measured, not assumed</b> (2026-08-12, and the
/// entry that asked for the work had one of them half wrong -- 00-STATUS P2-107). Anything
/// added here needs the same treatment: run it, do not reason about it.</para>
/// </summary>
public static class SerialErrorClassifier
{
    /// <summary>
    /// Classifies one exception. Anything unrecognised returns
    /// <see cref="SerialErrorKind.Unknown"/> -- <b>never guess</b>: a wrong guess hands the user
    /// a reason pointing the wrong way, which is worse than saying "unknown".
    ///
    /// <para>⛔ <b>Arm order is load-bearing in exactly one place.</b>
    /// <see cref="FileNotFoundException"/> derives from <see cref="IOException"/> and must be
    /// matched first.
    ///
    /// <para>And <b>that half is machine-guaranteed</b>: putting it after its base is
    /// <c>error CS8510, "the pattern is unreachable"</c> -- a build error, not a silent bug.
    /// Verified 2026-08-13 by actually making the cut.</para>
    ///
    /// <para>⚠️ <b>This paragraph first claimed the opposite</b> -- that the compiler stays quiet
    /// about a derived arm following its base. The mutation run answered COMPILE-ERROR and the
    /// claim was simply wrong. It was written from memory, never run: 03-conventions 9.5 applied
    /// to a code comment rather than to a 00-STATUS entry.</para>
    ///
    /// <para>⛔ <b>What is NOT machine-guaranteed is the arm existing at all</b> -- delete it and
    /// everything still compiles, while a mistyped port name goes back to telling the user to
    /// reconnect a device. That is what P2-107 was, and what the tests hold.</para>
    /// </summary>
    public static SerialErrorKind Classify(Exception? exception) => exception switch
    {
        null => SerialErrorKind.Unknown,

        // Port already in use (Windows: "Access to the port 'COM3' is denied"), and a device
        // node whose permissions are denied on Linux/macOS -- one type covers both.
        UnauthorizedAccessException => SerialErrorKind.AccessDenied,

        // The name does not resolve to a serial port at all. Measured sample:
        // "The given port name (SIM-A) does not resolve to a valid serial port."
        // ⚠ This is NOT the "that port is not here" case -- that one is below.
        ArgumentException => SerialErrorKind.PortNotFound,

        TimeoutException => SerialErrorKind.Timeout,

        // Handle already released -- the usual shape of reading on after a device is unplugged.
        ObjectDisposedException => SerialErrorKind.DeviceRemoved,

        // ⭐ A well-formed name with nothing behind it: "Could not find file 'COM99'."
        // ⛔ MUST stay above IOException, which it derives from. That is P2-107: it used to fall
        // through to DeviceRemoved, so a mistyped port name told the user to "reconnect the
        // device" -- a dead end for a name that was never right in the first place.
        // ⭐ PortNotFound is the right bucket for BOTH shapes that land here (a typo, and a port
        // that was unplugged and is still being opened by its old name): its wording says
        // "may have been unplugged, or the name may be wrong", which is exactly the ambiguity
        // the exception itself carries. The two are indistinguishable at this layer.
        FileNotFoundException => SerialErrorKind.PortNotFound,

        // Unplugging usually reaches the read loop as an IOException; InvalidOperationException
        // is "operated on a port that is not open", equally unusable either way.
        IOException => SerialErrorKind.DeviceRemoved,
        InvalidOperationException => SerialErrorKind.DeviceRemoved,

        _ => SerialErrorKind.Unknown
    };

    /// <summary>
    /// <b>读循环专用</b>：读被异常终止时，判断这是「我们要求的停止」还是「故障」，并归类。
    ///
    /// <returns><c>null</c> = 正常停止（不该报错）；非 null = 故障，且这是要显示给用户的原因。</returns>
    ///
    /// <b>⚠️ 判据只有一个：<paramref name="cancellationRequested"/>，不是异常类型。</b>
    /// 这一条是 2026-07-31 用真实 USB 适配器拔线时踩出来的（P1-36）——
    /// 原实现给 <see cref="OperationCanceledException"/> 单开一个 catch 并<b>无条件</b>
    /// 当成正常停止，于是设备拔出被记成 <c>Read loop exited normally</c>：
    /// 没有提示条、没有故障日志、状态栏一直停在「已连接」。
    /// <b>用户不去点发送，就永远不知道设备已经掉了。</b>
    ///
    /// <b>为什么 OCE 在这里不是「取消」</b>：本方法在
    /// <c>cancellationRequested == false</c> 时才会判为故障 —— 也就是
    /// <b>没有任何人要求过取消</b>。这种情况下读操作以取消收场，只可能是设备/句柄没了。
    ///
    /// ⚠️ <b>刻意不把 OCE 并进上面的 <see cref="Classify"/></b>：那个方法被连接与发送路径共用，
    /// 而那些地方的 OCE 是<b>真的取消</b>（用户点了断开）。归成 DeviceRemoved 会谎报。
    /// <b>判据依赖上下文，所以这条归类必须留在拿得到上下文的地方。</b>
    /// </summary>
    public static SerialErrorKind? ClassifyReadLoopStop(Exception exception, bool cancellationRequested)
    {
        // 我们自己要求停的 —— 不是故障，无论抛的是什么。
        if (cancellationRequested) return null;

        return exception is OperationCanceledException
            ? SerialErrorKind.DeviceRemoved
            : Classify(exception);
    }
}
