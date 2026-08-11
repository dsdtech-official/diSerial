namespace DiSerial.App.Services;

/// <summary>
/// 「本次运行是否处于开发模式」这一个事实，供 ViewModel 消费。
///
/// <b>为什么不直接注入 Infrastructure 的 <c>DeveloperOptions</c></b>：
/// 项目约定 App 对 Infrastructure 的引用<b>只出现在组合根</b>
/// （见 02-architecture 第二节，`ArchitectureTests` 守着 Core 侧的对应约束）。
/// ViewModel 直接 using Infrastructure 会开一个口子，后面就守不住了。
///
/// 组合根从 <c>DeveloperOptions.DebugMode</c> 映射出本类型并注册，
/// ViewModel 只依赖这个 App 层的小记录。将来开发开关变多，
/// 界面要区分「开了哪一个」时再扩展本类型即可，仍不必让 ViewModel 认识 Infrastructure。
/// </summary>
public sealed record DeveloperModeState(bool IsEnabled)
{
    /// <summary>未开启任何开发开关 —— 发布构建的取值。</summary>
    public static DeveloperModeState Disabled { get; } = new(false);
}
