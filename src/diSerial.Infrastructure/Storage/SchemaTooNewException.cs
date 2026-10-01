namespace DiSerial.Infrastructure.Storage;

// Report an unsupported schema.
public sealed class SchemaTooNewException(string path, int found, int supported)
    : Exception(
        $"The data file '{path}' was created by a newer version of this program "
        + $"(file format {found}, this version understands up to {supported}). "
        + "Upgrade the program, or point it at a different data directory.")
{
    public int Found { get; } = found;

    public int Supported { get; } = supported;

    public string Path { get; } = path;
}
