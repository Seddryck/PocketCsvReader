using System.Globalization;

namespace PocketCsvReader.ValueReader;

internal static class InvariantDecimalUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out decimal value)
        => decimal.TryParse(utf8, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
