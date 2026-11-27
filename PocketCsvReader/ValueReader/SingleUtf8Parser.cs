using System.Globalization;

namespace PocketCsvReader.ValueReader;

internal static class SingleUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out float value)
        => float.TryParse(utf8, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
}
