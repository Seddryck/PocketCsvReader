namespace PocketCsvReader.ValueReader;

internal static class TimeOnlyUtf8Parser
{
    internal static bool TryParse(ReadOnlySpan<byte> utf8, out TimeOnly value)
    {
        value = default;
        if (utf8.Length != 8 || utf8[2] != (byte)':' || utf8[5] != (byte)':'
            || !AsciiDigits.TryParse(utf8[..2], out var hour) || hour > 23
            || !AsciiDigits.TryParse(utf8.Slice(3, 2), out var minute) || minute > 59
            || !AsciiDigits.TryParse(utf8.Slice(6, 2), out var second) || second > 59)
            return false;

        value = new TimeOnly(hour, minute, second);
        return true;
    }
}
