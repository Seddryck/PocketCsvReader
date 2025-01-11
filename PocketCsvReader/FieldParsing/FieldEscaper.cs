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

    public FieldEscaper(CsvProfile Profile, ArrayPool<char>? pool = null)
        : this(Profile.Dialect.QuoteChar, Profile.Dialect.DoubleQuote, Profile.Dialect.EscapeChar, Profile.Dialect.Delimiter, pool)
    { }

    public FieldEscaper(char? quoteChar, bool doubleQuote, char? escapeChar, char delimiter, ArrayPool<char>? pool = null)
        => (QuoteChar, DoubleQuote, EscapeChar, Delimiter, Pool) = (quoteChar, doubleQuote, escapeChar, delimiter, pool);

    public ReadOnlySpan<char> Escape(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return Span<char>.Empty;

        var array = Pool?.Rent(value.Length) ?? new char[value.Length];
        var result = new Span<char>(array);
        int i = 0, j = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c == EscapeChar)
            {
                if (i + 1 == value.Length)
                    result[j++] = c;
                else if (value[i + 1] == EscapeChar || value[i + 1] == Delimiter || value[i + 1] == QuoteChar)
                    result[j++] = value[++i];
                else
                    result[j++] = c;
            }
            else if (c == QuoteChar && DoubleQuote)
            {
                if (value[i + 1] == QuoteChar)
                    result[j++] = value[++i];
                else
                    result[j++] = c;
            }
            else
                result[j++] = c;
            i++;
        }

        Pool?.Return(array);
        return result.Slice(0, j);
    }
}
