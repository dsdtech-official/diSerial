using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

// Represent a session choice.
public sealed class SessionTypeItem : ObservableObject
{
    public required SessionKind Kind { get; init; }

    public required ILocalizationService Localization { get; init; }

    public required string TitleKey { get; init; }

    public required string DescriptionKey { get; init; }

    public string? RequirementKey { get; init; }

    public required Func<SessionConfigViewModel> CreateConfig { get; init; }

    public required string IconPathData { get; init; }

    public System.Windows.Input.ICommand? Choose
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Avalonia.Media.Geometry Icon => field ??= Avalonia.Media.Geometry.Parse(IconPathData);

    public string Title => Localization[TitleKey];

    public string Description => Localization[DescriptionKey];

    public string? Requirement => RequirementKey is null ? null : Localization[RequirementKey];

    public bool HasRequirement => RequirementKey is not null;

    // Refresh localized labels.
    public void RefreshText() => OnPropertyChanged(string.Empty);
}

public interface ISessionTypeCatalog
{
    // Create session type choices.
    IReadOnlyList<SessionTypeItem> CreateItems();
}

// List available session types.
public sealed class SessionTypeCatalog(
    ILocalizationService localization,
    IEnumChoiceProvider enumChoices,
    IAppSettings settings) : ISessionTypeCatalog
{
    // Create session type choices.
    public IReadOnlyList<SessionTypeItem> CreateItems() =>
    [
        new SessionTypeItem
        {
            Kind = SessionKind.Terminal,
            Localization = localization,
            TitleKey = LocKeys.DialogNewSessionTerminal,
            DescriptionKey = LocKeys.DialogNewSessionTerminalDesc,
            IconPathData = OneBusIcon,
            CreateConfig = () => new TerminalConfigViewModel(localization, enumChoices, settings)
        },
        new SessionTypeItem
        {
            Kind = SessionKind.Monitor,
            Localization = localization,
            TitleKey = LocKeys.DialogNewSessionMonitor,
            DescriptionKey = LocKeys.DialogNewSessionMonitorDesc,
            RequirementKey = LocKeys.DialogNewSessionMonitorRequires,
            IconPathData = TwoBusIcon,
            CreateConfig = () => new MonitorConfigViewModel(localization, enumChoices, settings)
        }
    ];

    private const string OneBusIcon = "M3,6 H21 M12,6 V10 M9,10 H15 V16 H9 Z";

    private const string TwoBusIcon = "M3,4 H21 M3,20 H21 M12,4 V9 M12,15 V20 M9,9 H15 V15 H9 Z";
}
