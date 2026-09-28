namespace PocketCsvReader.FixedWidth.Configuration;

public enum FixedWidthPadding
{
    None,
    Left,
    Right,
    Both
}

public sealed record FixedWidthFieldDescriptor
(
    string Name,
    int Offset,
    int Width,
    FixedWidthPadding Padding = FixedWidthPadding.Right,
    char PaddingChar = ' '
);
