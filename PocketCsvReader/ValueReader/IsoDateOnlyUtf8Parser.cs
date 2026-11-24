namespace PocketCsvReader.ValueReader;

internal static class IsoDateOnlyUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out DateOnly value)
    {
        value = default;
        if (utf8.Length != 10 || utf8[4] != (byte)'-' || utf8[7] != (byte)'-'
            || !AsciiDigits.TryParse(utf8[..4], out var year)
            || !AsciiDigits.TryParse(utf8.Slice(5, 2), out var month)
            || !AsciiDigits.TryParse(utf8.Slice(8, 2), out var day)
            || year == 0 || month is < 1 or > 12
            || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        value = new DateOnly(year, month, day);
        return true;
    }
}
