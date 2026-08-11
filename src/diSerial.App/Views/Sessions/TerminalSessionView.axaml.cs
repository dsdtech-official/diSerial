using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiSerial.App.Views.Sessions;

public partial class TerminalSessionView : UserControl
{
    public TerminalSessionView() => InitializeComponent();

    /// <summary>
    /// Opening a terminal session puts the caret in the input box (P2-51 A1, user-approved
    /// 2026-08-03).
    ///
    /// <para><b>Why it is worth a call site at all.</b> Enter-to-send (spec 4.14) is only half
    /// a keyboard path while the user still has to reach for the mouse to give the box focus
    /// first. This is the other half.</para>
    ///
    /// <para>⛔ <b>Deliberately not done for monitor sessions</b> -- see
    /// <see cref="Panels.SendPanelView.FocusInput"/> for why the choice lives in the caller.</para>
    ///
    /// <para>⚠ V1.0 shows one session at a time (tabbed multi-session is out of scope, spec
    /// section 11), so "when the view loads" and "when the session becomes current" are the
    /// same moment. If tabs ever arrive, this needs to fire on activation too.</para>
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SendPanel.FocusInput();
    }
}
