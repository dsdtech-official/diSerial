using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Sessions;

// Represent a monitored channel.
public sealed partial class ChannelViewModel : ViewModelBase
{
    public const string ColorA = "#378ADD";

    public const string ColorB = "#1D9E75";

    private const int MaxDefaultAliasLength = 10;

    private const string UnixPortPrefix = "/dev/cu.";

    // Initialize a monitored channel.
    public ChannelViewModel(ChannelId id, SerialPortInfo port)
    {
        Id = id;
        Port = port;

        Alias = DefaultAliasFor(port.PortName);
    }

    // Build the default channel alias.
    public static string DefaultAliasFor(string portName)
    {
        if (string.IsNullOrEmpty(portName)) return string.Empty;

        var name = portName.StartsWith(UnixPortPrefix, StringComparison.Ordinal)
            ? portName[UnixPortPrefix.Length..]
            : portName;

        if (name.Length <= MaxDefaultAliasLength) return name;

        var lastDash = name.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < name.Length - 1)
        {
            name = name[(lastDash + 1)..];
        }

        return name.Length <= MaxDefaultAliasLength ? name : name[..MaxDefaultAliasLength];
    }

    public ChannelId Id { get; }

    [ObservableProperty]
    private SerialPortInfo _port;

    [ObservableProperty]
    private string _alias;

    [ObservableProperty]
    private long _bytesReceived;

    public string ColorHex => Id == ChannelId.A ? ColorA : ColorB;

    public string PortName => Port.PortName;

    public string DefaultAlias => DefaultAliasFor(PortName);

    public string InlineLabel => string.IsNullOrWhiteSpace(Alias) ? DefaultAlias : Alias;

    public bool HasCustomAlias =>
        !string.IsNullOrWhiteSpace(Alias) && !string.Equals(Alias, DefaultAlias, StringComparison.Ordinal);

    [ObservableProperty]
    private string _aliasPlaceholder = string.Empty;

    // Notify channel label changes.
    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(InlineLabel));
        OnPropertyChanged(nameof(HasCustomAlias));
    }
}
