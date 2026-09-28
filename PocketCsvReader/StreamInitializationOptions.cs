namespace PocketCsvReader;

public sealed record StreamInitializationOptions(bool ProbeEncoding = true)
{
    public static StreamInitializationOptions Default { get; } = new();
    public static StreamInitializationOptions ForwardOnly { get; } = new(ProbeEncoding: false);
}
