using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// One session-type card, plus the way to build that type's configuration step.
///
/// <para>⭐ <b>The text is resolved on every read, not captured at construction</b>
/// (2026-08-03). It used to be three <c>required string</c>s filled in by the catalog, which was
/// safe only because the cards lived exclusively in a <b>modal</b> dialog -- the language menu
/// could not be reached while they were on screen. <b>The same cards are now also on the main
/// window's empty state</b>, where the language menu is one click away, so captured strings
/// would sit there in the previous language after a switch.</para>
///
/// <para>⚠ <b>Reading live is not enough on its own</b>: nothing about swapping the culture
/// touches this object, so whoever holds a long-lived list has to call
/// <see cref="RefreshText"/> when the language changes. <c>MainWindowViewModel</c> does; the
/// dialog does not need to, because it builds a fresh list every time it opens.</para>
/// </summary>
public sealed class SessionTypeItem : ObservableObject
{
    public required SessionKind Kind { get; init; }

    public required ILocalizationService Localization { get; init; }

    public required string TitleKey { get; init; }

    public required string DescriptionKey { get; init; }

    /// <summary>Key for what the type needs, e.g. "any two serial ports". Null when it needs no line.</summary>
    public string? RequirementKey { get; init; }

    public required Func<SessionConfigViewModel> CreateConfig { get; init; }

    public string Title => Localization[TitleKey];

    public string Description => Localization[DescriptionKey];

    public string? Requirement => RequirementKey is null ? null : Localization[RequirementKey];

    public bool HasRequirement => RequirementKey is not null;

    /// <summary>
    /// Re-reads every piece of text after a language change.
    ///
    /// <para>Empty name = "all properties", the same signal
    /// <c>LocalizedViewModelBase.OnCultureChanged</c> sends, and for the same reason: these are
    /// computed properties with no backing field, so nothing else would make them re-evaluate.</para>
    /// </summary>
    public void RefreshText() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// The list of session types on offer.
///
/// <para>⭐ <b>This is the single place that knows the concrete types exist.</b> That is the
/// whole point: every caller iterates this list and calls
/// <see cref="SessionTypeItem.CreateConfig"/>, so <b>adding a session type is one entry here
/// plus one ViewModel plus one View</b> -- neither the dialog shell nor the main window changes.
/// <c>NewSessionDialogDecouplingTests</c> pins that across all four files.</para>
///
/// <para>⚠ <b>Two callers since 2026-08-03</b>: the new-session dialog's first step, and the
/// main window's empty state, which offers the same choice up front so the dialog can open
/// straight on step 2. Each builds its own list; they share no instances.</para>
///
/// <para>⚠ Deliberately <b>not</b> merged into <see cref="ISessionViewModelFactory"/>, which
/// maps a finished request onto a session. This one maps a type onto <i>the UI for asking the
/// user about it</i>. They grow together when a type is added, but they answer different
/// questions and live in different layers.</para>
/// </summary>
public interface ISessionTypeCatalog
{
    /// <summary>Builds a fresh set of items, with wording in the current language.</summary>
    IReadOnlyList<SessionTypeItem> CreateItems();
}

/// <inheritdoc />
public sealed class SessionTypeCatalog(
    ILocalizationService localization,
    IEnumChoiceProvider enumChoices,
    IAppSettings settings) : ISessionTypeCatalog
{
    public IReadOnlyList<SessionTypeItem> CreateItems() =>
    [
        // Terminal is first, and both callers preselect the first entry -- single serial port
        // stays the shortest path.
        new SessionTypeItem
        {
            Kind = SessionKind.Terminal,
            Localization = localization,
            TitleKey = LocKeys.DialogNewSessionTerminal,
            DescriptionKey = LocKeys.DialogNewSessionTerminalDesc,
            CreateConfig = () => new TerminalConfigViewModel(localization, enumChoices, settings)
        },
        new SessionTypeItem
        {
            Kind = SessionKind.Monitor,
            Localization = localization,
            TitleKey = LocKeys.DialogNewSessionMonitor,
            DescriptionKey = LocKeys.DialogNewSessionMonitorDesc,
            RequirementKey = LocKeys.DialogNewSessionMonitorRequires,
            CreateConfig = () => new MonitorConfigViewModel(localization, enumChoices, settings)
        }
    ];
}
