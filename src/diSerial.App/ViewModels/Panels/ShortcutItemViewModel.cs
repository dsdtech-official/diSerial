using System.Text;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DiSerial.Core.Abstractions;

namespace DiSerial.App.ViewModels.Panels;

// Represent a saved command.
public sealed class ShortcutItemViewModel : ViewModelBase
{
    private string _name;

    // Initialize shortcut commands.
    public ShortcutItemViewModel(Shortcut model, SendPanelViewModel? owner = null)
    {
        _name = model.Name;
        Id = model.Id;
        Payload = model.Payload;
        Tooltip = DescribePayload(model.Payload);

        SendCommand = Relay(() => owner?.SendShortcut(this));
        RenameCommand = Relay(() => owner?.RenameShortcutRequested(this));
        MoveToFrontCommand = Relay(() => owner?.MoveShortcutToFront(this));
        MoveForwardCommand = Relay(() => owner?.MoveShortcutForwardOne(this));
        DeleteCommand = Relay(() => owner?.DeleteShortcut(this));
    }

    // Wrap an action in a command.
    private static RelayCommand Relay(Action run) => new(run);

    public ICommand SendCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand MoveToFrontCommand { get; }

    public ICommand MoveForwardCommand { get; }

    public ICommand DeleteCommand { get; }

    public long Id { get; }

    public byte[] Payload { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Tooltip { get; }

    // Describe a saved payload.
    public static string DescribePayload(byte[] payload)
    {
        var hex = string.Join(' ', payload.Select(b => b.ToString("X2")));
        if (payload.Length == 0) return hex;

        var text = new StringBuilder(payload.Length);
        foreach (var b in payload)
        {
            switch (b)
            {
                case 0x0D: text.Append('␍'); break;
                case 0x0A: text.Append('␊'); break;
                case 0x09: text.Append('␉'); break;
                case >= 0x20 and <= 0x7E: text.Append((char)b); break;
                default: return hex;
            }
        }

        return hex + Environment.NewLine + "ASCII  " + text;
    }
}
