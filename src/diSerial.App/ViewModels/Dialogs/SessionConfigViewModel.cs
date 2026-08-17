using System.Collections.ObjectModel;
using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Panels;
using DiSerial.App.ViewModels.Sessions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// One session type's configuration step -- everything the new-session dialog needs to know
/// about a type, and nothing the dialog itself has to understand (user decision 2026-08-03).
///
/// <para><b>Why this exists.</b> Every per-type difference used to live on the dialog: the port
/// fields for both types sat on one ViewModel, <c>NewSessionResult</c> carried one nullable
/// field per type, and four methods branched on <see cref="SessionKind"/>. Adding a third type
/// meant touching all of them. Now a type is one subclass plus one matching View, and the
/// dialog shell names no type at all.</para>
///
/// <para>⭐ <b>The acceptance criterion for that claim is checkable</b>, and a test pins it:
/// adding a session type must not require editing <c>NewSessionDialogViewModel</c> or
/// <c>NewSessionDialog.axaml</c>. See <c>NewSessionDialogDecouplingTests</c>.</para>
///
/// <para>The matching View is found by naming convention -- <c>TerminalConfigViewModel</c> →
/// <c>TerminalConfigView</c> -- through the same <see cref="Composition.ViewLocator"/> that
/// already dispatches session views. This is not a new pattern; it is the one the session
/// layer has been using since the start.</para>
///
/// <para>⚠ <b>Serial-specific state lives here on purpose, and here is the seam if that ever
/// stops being true.</b> Both current types are serial, so <see cref="AvailablePorts"/> and
/// <see cref="Settings"/> sit on the shared base. A non-serial type (the TCP idea that was
/// deliberately deferred) has no baud rate and no port list -- at that point this class splits
/// into a bare contract plus a <c>SerialSessionConfigViewModel</c> holding these two members.
/// Building that split now would be structure for a type that does not exist.</para>
/// </summary>
public abstract class SessionConfigViewModel : LocalizedViewModelBase
{
    protected SessionConfigViewModel(ILocalizationService localization, IEnumChoiceProvider enumChoices)
        : base(localization)
    {
        Settings = new PortSettingsViewModel(enumChoices);
    }

    /// <summary>
    /// Baud rate, data bits, parity, stop bits, flow control.
    ///
    /// ⚠ <b>One instance per config, not one per dialog.</b> The dialog used to share a single
    /// instance across both types and swap its contents when the type changed, which is why
    /// switching type had to reload preferences by hand. Each type now owns its own.
    /// </summary>
    public PortSettingsViewModel Settings { get; }

    public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = [];

    /// <summary>Whether the current selection is complete enough to connect.</summary>
    public abstract bool CanConfirm { get; }

    /// <summary>Builds the creation request for this type.</summary>
    public abstract NewSessionResult BuildRequest();

    /// <summary>
    /// Stores this type's serial settings for next time.
    ///
    /// <b>Each type keeps its own slot</b>: a terminal talking to an Arduino usually wants
    /// 115200, a monitor sniffing Modbus usually wants 9600 8E1. Sharing one slot means every
    /// switch overwrites the other.
    /// </summary>
    public abstract void RememberSettings();

    /// <summary>
    /// Rebuilds <see cref="AvailablePorts"/> after a plug or unplug (C-03a), keeping whatever
    /// the user had already chosen.
    ///
    /// <b>Selections are held by port name, not by instance</b> -- the enumerator hands back
    /// fresh records each poll, so identity comparison would drop the selection on every tick.
    /// </summary>
    public abstract void ApplyPorts(IReadOnlyList<SerialPortInfo> ports);

    /// <summary>
    /// Turns a failed open into one sentence the user can act on.
    ///
    /// <para><b>Why the type answers this rather than the dialog.</b> The wording differs per
    /// type (a terminal names one port; a monitor has to say <i>which channel</i> failed), and
    /// the port name comes from this config's own selection rather than from the exception --
    /// so the terminal path that was verified end to end never had to change to carry it.</para>
    /// </summary>
    public abstract string DescribeFailure(SessionOpenFailure failure);

    /// <summary>Raised when <see cref="CanConfirm"/> may have changed, so the shell can re-ask.</summary>
    public event EventHandler? CanConfirmChanged;

    protected void RaiseCanConfirmChanged() => CanConfirmChanged?.Invoke(this, EventArgs.Empty);

    protected SerialPortInfo? ByName(string? portName) => portName is null
        ? null
        : AvailablePorts.FirstOrDefault(p => p.PortName == portName);

    /// <summary>
    /// Turns whatever is in a port box into the record a session will be opened with (P1-4).
    ///
    /// <para>⭐ <b>A name the enumerator knows keeps its
    /// <see cref="SerialPortInfo.Description"/></b>, so the session still shows "Prolific
    /// USB-to-Serial"; anything else becomes a bare record carrying just the typed name.</para>
    ///
    /// <para>⛔ <b>An unknown name is deliberately NOT refused here.</b> That is the whole point
    /// of the feature: on macOS users know the exact <c>/dev/cu.usbserial-XXXX</c> path, and a
    /// dialog that only accepts what we enumerated would put our own filtering between them and
    /// their device. A name with nothing behind it fails at open time with "that serial port does
    /// not exist -- it may have been unplugged, or the name may be wrong", which is the sentence
    /// P2-107 exists to make reachable.</para>
    ///
    /// <para>⚠️ <b>Matching is exact, not case-insensitive.</b> Typing <c>com5</c> when the list
    /// has <c>COM5</c> yields a bare record rather than the enumerated one -- the port still
    /// opens (Windows does not care about case), it just shows no description. Folding case here
    /// would mean deciding it for <c>/dev/</c> paths too, where it is wrong.</para>
    /// </summary>
    protected SerialPortInfo? FromText(string? text)
    {
        var name = text?.Trim();

        return string.IsNullOrEmpty(name)
            ? null
            : ByName(name) ?? new SerialPortInfo { PortName = name };
    }

    protected void Repopulate(IReadOnlyList<SerialPortInfo> ports)
    {
        AvailablePorts.Clear();
        foreach (var port in ports) AvailablePorts.Add(port);
    }

    /// <summary>Shared wording for a failure, before each type wraps it in its own sentence.</summary>
    protected string DescribeReason(SessionOpenFailure failure) =>
        SessionErrorPresenter.Describe(failure.Kind, Localization);
}
