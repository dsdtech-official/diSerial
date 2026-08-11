namespace DiSerial.App.Controls;

/// <summary>
/// 「是否贴在内容末尾」的判定，只依赖三个几何量：纵向偏移、内容总高、视口高。
///
/// 刻意不引用任何控件类型 —— 显示区日后换成自绘控件
/// （override Render + 虚拟化）时，新控件只要报得出这三个数就能原样复用，
/// 跟随行为不必跟着重写一遍。<see cref="AutoScrollViewer"/> 是它当前的唯一宿主。
/// </summary>
internal static class ScrollFollow
{
    /// <summary>
    /// 判定「贴在底部」的容差（DIP）。
    ///
    /// 只需覆盖布局取整带来的亚像素误差即可。取大了会把用户轻微的上滚
    /// 也算作贴底，从而把人拽回底部；而一个滚轮档位远大于此值，
    /// 因此任何有意的上滚都会被判成离开底部。
    /// </summary>
    private const double BottomTolerance = 4.0;

    /// <summary>内容高于视口时能滚到的最大纵向偏移；内容不足一屏时为 0。</summary>
    public static double MaxOffset(double extentHeight, double viewportHeight)
        => Math.Max(0.0, extentHeight - viewportHeight);

    public static bool IsAtBottom(double offsetY, double extentHeight, double viewportHeight)
        => offsetY >= MaxOffset(extentHeight, viewportHeight) - BottomTolerance;

    /// <summary>
    /// Whether follow mode should be on after a scroll change: at the bottom as measured
    /// <b>before</b> the change, <b>or</b> at the bottom as measured <b>after</b> it.
    ///
    /// <para><b>Both halves are load-bearing and neither is redundant</b>:</para>
    /// <list type="bullet">
    /// <item>Appending data pushes the bottom away while the user has not moved. Frames arrive
    /// in batches and one batch is dozens of rows — far more than the tolerance — so judging
    /// by the geometry <i>after</i> the change would read every single batch as "the user
    /// scrolled up". The <i>before</i> half carries this case.</item>
    /// <item>Growing the window enlarges the viewport, and the offset is clamped down to the
    /// new maximum. Judged by the geometry <i>before</i> the change that clamped offset is
    /// well short of the old bottom. Only the <i>after</i> half recognises it as still at the
    /// end.</item>
    /// </list>
    ///
    /// <para>When the user really does scroll up, neither half holds and follow mode stops.</para>
    ///
    /// <para>⚠️ <b>Deliberately a pure function rather than logic inside
    /// <see cref="AutoScrollViewer"/></b> (P1-9, user decision 2026-08-02). It was written
    /// inline there, which put the one rule most likely to be broken out of reach of the tests
    /// — this project does not run the Avalonia runtime under test. It also belongs here for
    /// the reason in the type remarks: the self-drawn display (P1-6) has to reproduce this
    /// rule, and reproducing it is exactly how the two halves get separated again.</para>
    /// </summary>
    public static bool ShouldFollow(
        double offsetY,
        double extentHeightBefore,
        double viewportHeightBefore,
        double extentHeightAfter,
        double viewportHeightAfter)
        => IsAtBottom(offsetY, extentHeightBefore, viewportHeightBefore)
           || IsAtBottom(offsetY, extentHeightAfter, viewportHeightAfter);
}
