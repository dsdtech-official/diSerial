namespace DiSerial.Core.Abstractions;

// Describe a stored setting.
public sealed record SettingsRowInfo(string Note, string ValueType, string DefaultValue);

public interface ISettingsStore
{
    // Read all stored settings.
    IReadOnlyDictionary<string, string> LoadAll();

    // Insert or update settings.
    void Upsert(IReadOnlyDictionary<string, string> changedRows, IReadOnlyDictionary<string, SettingsRowInfo> info);

    // Sync the settings catalog.
    void Sync(IReadOnlyDictionary<string, SettingsRowInfo> catalog);

}
