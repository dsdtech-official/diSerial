using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace DiSerial.App.Controls;

// Follow the end of content.
public sealed class AutoScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

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

    // Handle scrolling changes.
    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);

        var following = ScrollFollow.ShouldFollow(
            Offset.Y,
            Extent.Height - e.ExtentDelta.Y, Viewport.Height - e.ViewportDelta.Y,
            Extent.Height, Viewport.Height);

        SetCurrentValue(IsFollowingProperty, following);

        if (following)
        {
            ScrollToBottom();
        }
    }

    // Handle control property changes.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsFollowingProperty && change.GetNewValue<bool>())
        {
            ScrollToBottom();
        }
    }

    // Scroll to the latest content.
    private void ScrollToBottom()
    {
        SetCurrentValue(OffsetProperty,
            new Vector(Offset.X, ScrollFollow.MaxOffset(Extent.Height, Viewport.Height)));
    }
}
