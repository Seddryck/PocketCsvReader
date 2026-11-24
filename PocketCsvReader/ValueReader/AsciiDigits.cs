namespace PocketCsvReader.ValueReader;

internal static class AsciiDigits
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out int value)
    {
        value = 0;
        foreach (var character in utf8)
        {
            var digit = character - '0';
            if ((uint)digit > 9)
                return false;

            value = (value * 10) + digit;
        }
        return true;
    }
}
