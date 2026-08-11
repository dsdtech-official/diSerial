namespace DiSerial.App.ViewModels.Panels;

/// <summary>
/// 一行数据的着色依据（T-04，规格见 docs/01-spec.md 4.12）。
///
/// <b>颜色的含义恒为「谁在说话」</b> —— 这是合并时间轴相对两个独立终端的核心价值，
/// 在单串口终端里同样成立，只是说话人从「通道 A / B」变成「对端 / 我」。
///
/// ⚠️ <b>刻意不叫 <c>FrameColor</c></b>：具体用哪个色值是视图层的事
/// （<c>FrameAccentToBrushConverter</c>），本枚举只说「这一行属于哪一类」。
/// </summary>
public enum FrameAccent
{
    /// <summary>终端会话收到的数据 —— 不着色，用默认前景色。</summary>
    Rx,

    /// <summary>终端会话发出的数据（T-02 本地回显）—— 紫色。</summary>
    Tx,

    /// <summary>监听会话通道 A —— 蓝。</summary>
    ChannelA,

    /// <summary>监听会话通道 B —— 绿。</summary>
    ChannelB
}
