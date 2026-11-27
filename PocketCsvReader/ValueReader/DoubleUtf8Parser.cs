using System.Globalization;

namespace PocketCsvReader.ValueReader;

internal static class DoubleUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out double value)
        => double.TryParse(utf8, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
}
