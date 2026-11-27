using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
internal class FieldEscaper
{
    protected ArrayPool<char>? Pool { get; }
    protected char? QuoteChar { get; }
    protected char? EscapeChar { get; }
    protected char Delimiter { get; }
    protected bool DoubleQuote { get; }

    public FieldEscaper(CsvProfile profile, ArrayPool<char>? pool = null)
        : this(profile.Dialect.QuoteChar, profile.Dialect.DoubleQuote, profile.Dialect.EscapeChar, profile.Dialect.Delimiter, pool)
    { }

    public FieldEscaper(char? quoteChar, bool doubleQuote, char? escapeChar, char delimiter, ArrayPool<char>? pool = null)
        => (QuoteChar, DoubleQuote, EscapeChar, Delimiter, Pool) = (quoteChar, doubleQuote, escapeChar, delimiter, pool);

    public ReadOnlySpan<char> Escape(ReadOnlySpan<char> value)
        => EscapeMemory(value).Span;

    internal ReadOnlyMemory<char> EscapeMemory(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return ReadOnlyMemory<char>.Empty;

        // The returned memory escapes this method, so its backing storage must
        // remain owned by the caller instead of being returned to a pool.
        var array = new char[value.Length];
        int length = 0;
        int i = 0;
        while (i < value.Length)
        {
            if (i + 1 < value.Length && IsEscapedPair(value[i], value[i + 1]))
                i++;
            array[length++] = value[i++];
        }

        return array.AsMemory(0, length);
    }

    internal int UnescapeUtf8(ReadOnlySpan<byte> value, Span<byte> destination)
    {
        // Native CSV syntax is ASCII, so bytes of multibyte code points never match.
        int length = 0;
        int i = 0;
        while (i < value.Length)
        {
            if (i + 1 < value.Length && IsEscapedPair((char)value[i], (char)value[i + 1]))
                i++;
            destination[length++] = value[i++];
        }
        return length;
    }

    private bool IsEscapedPair(char first, char second)
        => first == EscapeChar
            ? second == EscapeChar || second == Delimiter || second == QuoteChar
            : DoubleQuote && first == QuoteChar && second == QuoteChar;
}
