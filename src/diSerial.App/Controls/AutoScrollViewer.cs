using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace DiSerial.App.Controls;

/// <summary>
/// 跟随内容末尾滚动的 ScrollViewer（补齐 C-08 的自动滚动部分）。
///
/// 两条行为缺一不可：
/// 1. 内容变长时滚到末尾 —— 否则实时监听看不到当前流量，核心场景不成立；
/// 2. 用户手动上滚即停止跟随，滚回底部再自动恢复 —— 排查现场问题时经常
///    要停下来回看前几帧，若被一直拽回底部则完全没法用。
///
/// <b>跟随状态不另设记忆</b>：每次滚动变化都由几何量重新推导（见
/// <see cref="OnScrollChanged"/>）。因此不存在「正在程序性滚动」这类标志位，
/// 也就不会出现标志位漏清导致跟随永久失灵。
///
/// 之所以做成子类而不是附加属性：附加属性只在值<i>变化</i>时才有通知，
/// 绑定源初值恰好等于默认值时收不到，事件订阅时机会漏。子类在构造后即生效。
///
/// 样式沿用基类（<see cref="StyleKeyOverride"/>），本控件不带模板。
/// </summary>
public sealed class AutoScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>
    /// 当前是否跟随末尾。默认双向绑定 —— 用户上滚时由本控件写回 ViewModel
    /// （<c>LogPanelViewModel.AutoScroll</c>），ViewModel 置回 true 时立即归位。
    /// </summary>
    public static readonly StyledProperty<bool> IsFollowingProperty =
        AvaloniaProperty.Register<AutoScrollViewer, bool>(
            nameof(IsFollowing),
            defaultValue: true,
            defaultBindingMode: BindingMode.TwoWay);

    public bool IsFollowing
    {
        get => GetValue(IsFollowingProperty);
        set => SetValue(IsFollowingProperty, value);
    }

    /// <summary>
    /// Called whenever the scroll offset, the content height or the viewport height changes.
    ///
    /// <para>All this method does is turn Avalonia's deltas into "before" and "after"
    /// geometry and hand both to <see cref="ScrollFollow.ShouldFollow"/>. <b>The rule itself
    /// lives there on purpose</b> — it used to be written inline here, where the test suite
    /// could not reach it because this type derives from <c>ScrollViewer</c> and the App tests
    /// deliberately stay off the Avalonia runtime (P1-9, moved 2026-08-02). Why each half of
    /// that rule is load-bearing is documented on <c>ShouldFollow</c> itself, next to the
    /// cases that pin it.</para>
    ///
    /// <para>⚠️ Avalonia reports <i>deltas</i>, so "before" is the current value minus the
    /// delta. Both probes use the <b>current</b> <c>Offset.Y</c> — after a window resize the
    /// offset has already been clamped to the new maximum, and that clamped value is exactly
    /// what the "after" half has to judge.</para>
    /// </summary>
    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);

        var following = ScrollFollow.ShouldFollow(
            Offset.Y,
            Extent.Height - e.ExtentDelta.Y, Viewport.Height - e.ViewportDelta.Y,
            Extent.Height, Viewport.Height);

        // SetCurrentValue：改当前值但不顶掉绑定，双向绑定照常回写 ViewModel。
        SetCurrentValue(IsFollowingProperty, following);

        if (following)
        {
            ScrollToBottom();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // ViewModel 侧重新打开跟随时立刻归位，不必等下一批数据到达。
        if (change.Property == IsFollowingProperty && change.GetNewValue<bool>())
        {
            ScrollToBottom();
        }
    }

    /// <summary>
    /// 只改纵向偏移。基类的 ScrollToEnd() 会把横向一并归零 ——
    /// 数据列很宽，用户右移查看长十六进制串时会被每一批新数据拽回最左边。
    /// </summary>
    private void ScrollToBottom()
    {
        // 目标值与当前值相同时属性系统不发通知，因此这里不会与
        // OnScrollChanged 形成回环。
        SetCurrentValue(OffsetProperty,
            new Vector(Offset.X, ScrollFollow.MaxOffset(Extent.Height, Viewport.Height)));
    }
}
