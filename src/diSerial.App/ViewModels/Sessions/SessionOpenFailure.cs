using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

/// <summary>
/// 「建立会话时打不开端口」的结构化失败，供**新建会话对话框**组装提示用。
///
/// <b>为什么不直接把异常传上去</b>：对话框要显示的是
/// 「监听模式需要同时打开 2 个串口。**通道 B（COM7）** 无法打开：**端口不存在**。」——
/// 三个要素分别来自三处：
///
/// <list type="bullet">
///   <item><b>通道</b> —— 只有 <see cref="SerialPortOpenException"/> 知道（监听要开两个口）</item>
///   <item><b>分类</b> —— 由 <see cref="SerialErrorClassifier"/> 从底层异常得出，
///         再由 <c>SessionErrorPresenter</c> 映射成本地化文案</item>
///   <item><b>原文</b> —— 英文框架消息，<b>只进日志，不进界面</b>（01-spec 4.7 的口径）</item>
/// </list>
///
/// 把它们打成一个记录，对话框就不必自己 catch 异常、也不必知道异常类型。
/// </summary>
/// <param name="Channel">
/// 失败的那一路。终端会话只有一个端口，为 <see cref="ChannelId.None"/>。
///
/// ⚠️ 监听是**顺序打开**的（先 A 后 B），所以「两个都打不开」在这里表现为
/// 「A 打不开」—— B 根本没被尝试过。提示按实际失败的那一路说，不臆测另一路。
/// </param>
/// <param name="Kind">分类结果，界面显示它。</param>
/// <param name="Detail">底层异常原文。<b>只用于日志</b>。</param>
public sealed record SessionOpenFailure(ChannelId Channel, SerialErrorKind Kind, string? Detail)
{
    public static SessionOpenFailure From(Exception exception) => exception is SerialPortOpenException open
        // 监听：通道来自异常；分类要看 inner（外层那句是我们自己拼的英文，分类不出东西）
        ? new SessionOpenFailure(
            open.Channel,
            SerialErrorClassifier.Classify(open.InnerException ?? open),
            (open.InnerException ?? open).Message)

        // 终端：只有一个端口，端口名由对话框从自己的选择里取 ——
        // 所以 TerminalCaptureSession 不必为此改动（它是唯一端到端验证过的链路）。
        : new SessionOpenFailure(
            ChannelId.None,
            SerialErrorClassifier.Classify(exception),
            exception.Message);
}
