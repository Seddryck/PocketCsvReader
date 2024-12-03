using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
internal class StringMapper
{
    protected internal CsvProfile Profile { get; private set; }
    private FieldEscaper FieldEscaper { get; }

    protected bool HandlesSpecialValues { get; }
    protected bool UnescapesChars { get; }

    protected PoolString FetchString { get; }
    private static readonly PoolString defaultPoolString = (ReadOnlySpan<char> span) => span.ToString();

    public StringMapper(CsvProfile profile)
        : this(profile, ArrayPool<char>.Shared) { }

    public StringMapper(CsvProfile profile, ArrayPool<char>? pool)
        => (Profile, FetchString, HandlesSpecialValues, UnescapesChars, FieldEscaper)
            = (profile, profile.ParserOptimizations.PoolString ?? defaultPoolString
                , profile.ParserOptimizations.HandleSpecialValues, profile.ParserOptimizations.UnescapeChars
                , new FieldEscaper(profile, pool));


    public string? Parse(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
    {
        if (HandlesSpecialValues)
        {
            if (buffer.Length == 0)
                return Profile.EmptyCell;
            else if (!isEscapedField && !wasQuotedField)
            {
                var strField = FetchString(buffer);
                if (Profile.Sequences.TryGetValue(strField, out var value))
                    return value;
                return strField;
            }
        }

        if (UnescapesChars && isEscapedField)
        {
            var span = FieldEscaper.Escape(buffer);
            return FetchString(span);
        }

        return FetchString(buffer);
    }
}
