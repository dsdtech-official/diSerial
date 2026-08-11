namespace DiSerial.Core.Models;

/// <summary>
/// 把底层抛出的异常归类为 <see cref="SerialErrorKind"/>。
///
/// <b>为什么放在 Core</b>：Infrastructure 在读循环里需要它（把故障随事件上报），
/// App 在 ViewModel 的 catch 里也需要它（连接与发送失败就在那里被接住）——
/// 而 App 不引用 Infrastructure（组合根除外），两边共用的东西只能落在 Core。
///
/// 本类只依赖 BCL 异常类型，<b>不引用 System.IO.Ports</b>：
/// 打开一个不存在的端口抛的是 <see cref="ArgumentException"/>，
/// 端口被占用抛的是 <see cref="UnauthorizedAccessException"/> —— 都是 BCL 类型。
/// 因此 ArchitectureTests 的平台库禁令不受影响。
/// </summary>
public static class SerialErrorClassifier
{
    /// <summary>
    /// 归类一个异常。无法识别时返回 <see cref="SerialErrorKind.Unknown"/> ——
    /// <b>绝不猜测</b>：猜错会给用户一个指向错误方向的原因，比说「未知」更糟。
    /// </summary>
    public static SerialErrorKind Classify(Exception? exception) => exception switch
    {
        null => SerialErrorKind.Unknown,

        // 端口被占用（Windows：Access to the port 'COM3' is denied）
        // 与 Linux/macOS 的设备节点权限被拒，抛的是同一个类型。
        UnauthorizedAccessException => SerialErrorKind.AccessDenied,

        // 端口名无法解析为有效串口。实测样本：
        // "The given port name (SIM-A) does not resolve to a valid serial port."
        ArgumentException => SerialErrorKind.PortNotFound,

        TimeoutException => SerialErrorKind.Timeout,

        // 句柄已释放 —— 设备拔出后继续读写的典型表现。
        ObjectDisposedException => SerialErrorKind.DeviceRemoved,

        // 设备拔出时读循环拿到的多是 IOException；
        // InvalidOperationException 则是「端口未打开就操作」，同样归为不可用。
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
