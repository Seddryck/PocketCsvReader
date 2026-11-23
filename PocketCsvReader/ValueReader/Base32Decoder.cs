namespace PocketCsvReader.FieldParsing;

internal static class Base32Decoder
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string Base32HexAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    internal static byte[] DecodeBase32(ReadOnlySpan<char> text) => Decode(text, Base32Alphabet);
    internal static byte[] DecodeBase32Hex(ReadOnlySpan<char> text) => Decode(text, Base32HexAlphabet);

    private static byte[] Decode(ReadOnlySpan<char> text, ReadOnlySpan<char> alphabet)
    {
        if (text.IsEmpty)
            return [];

        var symbols = text.TrimEnd('=');
        var expectedPadding = (symbols.Length % 8) switch
        {
            0 => 0,
            2 => 6,
            4 => 4,
            5 => 3,
            7 => 1,
            _ => -1,
        };
        if (text.Length - symbols.Length != expectedPadding)
            throw new FormatException("Invalid Base32 length or padding.");

        var bytes = new byte[(int)((long)symbols.Length * 5 / 8)];
        var pendingBits = 0;
        var pendingBitCount = 0;
        var written = 0;
        foreach (var symbol in symbols)
        {
            var value = alphabet.IndexOf(char.IsAsciiLetterLower(symbol) ? char.ToUpperInvariant(symbol) : symbol);
            if (value < 0)
                throw new FormatException("Invalid Base32 character.");

            pendingBits = (pendingBits << 5) | value;
            pendingBitCount += 5;
            if (pendingBitCount >= 8)
            {
                pendingBitCount -= 8;
                bytes[written++] = (byte)(pendingBits >> pendingBitCount);
                pendingBits &= (1 << pendingBitCount) - 1;
            }
        }

        if (pendingBits != 0)
            throw new FormatException("Invalid Base32 trailing bits.");

        return bytes;
    }
}
