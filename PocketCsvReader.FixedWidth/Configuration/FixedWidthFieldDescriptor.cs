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
)
{
    internal ReadOnlySpan<char> TrimPadding(ReadOnlySpan<char> span)
    {
        var start = 0;
        var end = span.Length;
        if (Padding is FixedWidthPadding.Left or FixedWidthPadding.Both)
        {
            while (start < end && span[start] == PaddingChar)
                start++;
        }

        if (Padding is FixedWidthPadding.Right or FixedWidthPadding.Both)
        {
            while (end > start && span[end - 1] == PaddingChar)
                end--;
        }

        return span.Slice(start, end - start);
    }
}
