namespace PocketCsvReader;

public enum CsvInputMode
{
    Auto,
    DecodedChars,
    Utf8Bytes
}

public sealed record StreamInitializationOptions(bool ProbeEncoding = true, CsvInputMode InputMode = CsvInputMode.DecodedChars)
{
    // Preserve deconstruction supported by the original single-option record.
    public void Deconstruct(out bool probeEncoding) => probeEncoding = ProbeEncoding;
    public static StreamInitializationOptions Default { get; } = new();
    public static StreamInitializationOptions ForwardOnly { get; } = new(ProbeEncoding: false);
    public static StreamInitializationOptions Utf8 { get; } = new(ProbeEncoding: false, InputMode: CsvInputMode.Utf8Bytes);
}
