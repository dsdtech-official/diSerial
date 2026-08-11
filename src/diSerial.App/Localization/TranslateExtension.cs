using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace DiSerial.App.Localization;

/// <summary>
/// XAML 标记扩展：<c>{loc:Translate Key}</c>
///
/// 产生一个指向 <see cref="LocalizedString.Value"/> 的绑定，因此语言切换时
/// 会自动刷新 —— 这是「运行时实时切换」相对 <c>{x:Static}</c> 的关键差别
/// （后者取值后即固化，必须重启才能换语言）。
///
/// ⚠️ 为什么不直接绑索引器 <c>[Key]</c>：Avalonia 对 CLR 索引器的变更通知
/// 要求特定属性名（"Item[]"），「所有属性均已变更」的空字符串通知覆盖不到，
/// 结果是设置能保存、界面却不刷新。绑普通属性可彻底避开这一语义差异。
///
/// 绑定源是本地化服务而非 DataContext，故与各 View 上的 x:DataType
/// 编译绑定互不干扰。
/// </summary>
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension() { }

    public TranslateExtension(string key) => Key = key;

    /// <summary>资源键，如 <c>Menu.File</c>。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var source = LocalizationService.Current?.GetBindable(Key);

        // 设计器预览等场景下服务尚未初始化，退回显示键名而不是抛异常。
        if (source is null) return Key;

        return new Binding
        {
            Mode = BindingMode.OneWay,
            Source = source,
            Path = nameof(LocalizedString.Value)
        };
    }
}
