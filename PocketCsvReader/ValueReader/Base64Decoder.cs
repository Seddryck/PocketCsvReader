namespace PocketCsvReader.FieldParsing;

internal static class Base64Decoder
{
    internal static byte[] Decode(ReadOnlySpan<char> text)
    {
        // Count only characters read by the runtime decoder to size the output exactly.
        var encodedLength = 0;
        var padding = 0;
        foreach (var character in text)
        {
            if (character is ' ' or '\t' or '\r' or '\n')
                continue;

            encodedLength++;
            padding = character == '=' ? padding + 1 : 0;
        }

        if (encodedLength == 0)
            return [];

        if (encodedLength % 4 != 0 || padding > 2)
            throw new FormatException("Invalid Base64 length or padding.");

        var bytes = new byte[(encodedLength / 4 * 3) - padding];
        if (!Convert.TryFromBase64Chars(text, bytes, out var bytesWritten) || bytesWritten != bytes.Length)
            throw new FormatException("Invalid Base64 data.");

        return bytes;
    }
}
