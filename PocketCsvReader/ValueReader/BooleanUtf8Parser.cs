using System.Buffers.Text;

namespace PocketCsvReader.ValueReader;

internal static class BooleanUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out bool value)
        => Utf8Parser.TryParse(utf8, out value, out var consumed) && consumed == utf8.Length;
}
