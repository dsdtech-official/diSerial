using Avalonia.Controls;

namespace DiSerial.App.Views.Dialogs;

/// <summary>
/// Wires a dialog view model's "close me" event to <see cref="Window.Close()"/>.
///
/// <para><b>What it fixes</b> (P2-37, 2026-08-05). Three dialogs carried the same eight
/// lines verbatim:
/// <code>
/// DataContextChanged += (_, _) =&gt; { if (DataContext is TVm vm) vm.CloseRequested += (_, _) =&gt; Close(); };
/// </code>
/// with two defects in it. The handler was an <b>anonymous lambda</b>, so no <c>-=</c> was
/// possible even in principle — the view model held the window alive for its own lifetime.
/// And <c>DataContextChanged</c> is not a once-only event: a second assignment stacked a
/// second subscription, after which one request closed the window twice.</para>
///
/// <para>⭐ <b>The shape is deliberately the one from
/// <c>SessionViewModel.Subscribe</c></b> (P2-44): the caller cannot subscribe without
/// handing back the matching unsubscribe, so forgetting means <i>not subscribing</i> —
/// a dialog that refuses to close, which is impossible to miss. The failure mode moved from
/// invisible to obvious.</para>
///
/// <para>⚠️ <b>What it does not buy, stated plainly</b>: a raw <c>vm.CloseRequested +=</c>
/// written directly in a code-behind still leaks, exactly as before. This makes the right
/// way the short way; it does not make the wrong way impossible.</para>
///
/// <para><b>Why a static helper rather than a shared base class</b>: a base class would have
/// to appear as the root element of all three <c>.axaml</c> files, and the three
/// "close me" events do not share a signature anyway (<c>EventHandler</c> for two,
/// <c>EventHandler&lt;bool&gt;</c> for <c>NewSessionDialogViewModel</c>). The per-dialog
/// lambda below is where that difference belongs; everything that has to be <i>correct</i>
/// lives here, once.</para>
/// </summary>
internal static class DialogCloseBinding
{
    /// <param name="window">The dialog to close.</param>
    /// <param name="attach">
    /// Subscribe to the view model's close event and <b>return the matching unsubscribe</b>.
    /// Runs whenever the data context becomes a <typeparamref name="TViewModel"/>.
    /// </param>
    public static void Bind<TViewModel>(Window window, Func<TViewModel, Action> attach)
        where TViewModel : class
    {
        Action? release = null;

        void Rebind(object? sender, EventArgs e)
        {
            // Release first: DataContextChanged can fire more than once, and the old
            // subscription must not survive into the new context.
            release?.Invoke();
            release = window.DataContext is TViewModel vm ? attach(vm) : null;
        }

        void Released(object? sender, EventArgs e)
        {
            release?.Invoke();
            release = null;
        }

        window.DataContextChanged += Rebind;
        window.Closed += Released;
    }
}
