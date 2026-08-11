using Avalonia.Controls;
using Avalonia.Threading;

namespace DiSerial.App.Views.Panels;

public partial class SendPanelView : UserControl
{
    public SendPanelView() => InitializeComponent();

    /// <summary>
    /// Puts the caret in the input box (P2-51 A1, user-approved 2026-08-03).
    ///
    /// <para><b>Why the caller decides, not this panel.</b> Only terminal sessions want this.
    /// In a monitor session, typing is injection into a live bus and M-09's whole design is to
    /// make that deliberate rather than convenient -- the same reason Enter is excluded there
    /// (spec 4.14, promise 1). Exposing a method and letting <c>TerminalSessionView</c> call it
    /// makes "terminal only" structural: the monitor view has no call site, so there is no
    /// runtime condition anyone can get wrong later.</para>
    ///
    /// <para>⚠ Posted rather than called inline. At <c>Loaded</c> the visual tree exists but
    /// focus is still settling, and a <c>Focus()</c> that lands mid-cycle is silently dropped --
    /// the same shape as <see cref="OnHistorySelectionChanged"/> below, which cost a round to
    /// find. Nothing here reports failure, so the check is the caret on screen.</para>
    /// </summary>
    public void FocusInput() =>
        Dispatcher.UIThread.Post(() => InputBox.Focus(), DispatcherPriority.Input);

    /// <summary>
    /// Puts the history dropdown back to its placeholder after a pick.
    ///
    /// <para><b>Why the ViewModel cannot do this on its own.</b> <c>SelectedHistoryEntry</c>
    /// already resets itself to <c>null</c>, and the ViewModel tests prove that it does — but
    /// Avalonia discards that write because it arrives <i>during</i> its own selection-changed
    /// cycle. The collapsed box therefore kept showing the row that had just been picked, and
    /// since every row carries a delete button, the closed control ended up displaying a live
    /// "×" belonging to an entry the user was no longer looking at.</para>
    ///
    /// <para>⚠️ <b>Measured on the running application, not deduced</b> (2026-08-02): every
    /// ViewModel assertion was green while the control on screen was wrong. That is the split
    /// this project keeps re-learning — unit tests check "the code matches my understanding",
    /// only the real window checks "my understanding matches Avalonia".</para>
    ///
    /// <para>Posting is the whole trick: the assignment has to land after the cycle that is
    /// rejecting it. Setting it inline here fails in exactly the same way the ViewModel does.
    /// The null guard ends the recursion — clearing the selection raises this event once more,
    /// and that pass has nothing left to do.</para>
    /// </summary>
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is null) return;

        Dispatcher.UIThread.Post(() => combo.SelectedItem = null, DispatcherPriority.Background);
    }
}
