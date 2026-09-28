namespace PocketCsvReader.FixedWidth.Configuration;

public sealed record FixedWidthDescriptor
(
    IReadOnlyList<FixedWidthFieldDescriptor> Fields,
    string LineTerminator,
    bool Header = false,
    bool AllowTrailingCharacters = false
);
