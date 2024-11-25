using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;

public delegate string PoolString(ReadOnlySpan<char> memory);

public class ArrayOfStringMapper
{
    protected internal CsvProfile Profile { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    protected PoolString FetchString { get; }
    protected bool HandlesSpecialValues { get; }
    protected bool UnescapesChars { get; }

    private static readonly PoolString defaultPoolString = (ReadOnlySpan<char> span) => span.ToString();

    public ArrayOfStringMapper(CsvProfile profile)
        : this(profile, ArrayPool<char>.Shared) { }

    public ArrayOfStringMapper(CsvProfile profile, ArrayPool<char>? pool)
        => (Profile, Pool, FetchString, HandlesSpecialValues, UnescapesChars)
            = (profile, pool, profile.ParserOptimizations.PoolString ?? defaultPoolString
                , profile.ParserOptimizations.HandleSpecialValues, profile.ParserOptimizations.UnescapeChars);

    public string?[] Map(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans)
    {
        var fields = new string?[fieldSpans.Count()];
        var index = 0;
        foreach (var fieldSpan in fieldSpans)
            fields[index++] = Map(span, fieldSpan);
        return fields;
    }

    public string? Map(ReadOnlySpan<char> span, FieldSpan fieldSpan)
        => ExtractField(span.Slice(fieldSpan.Start, fieldSpan.Length), fieldSpan.IsEscaped, fieldSpan.WasQuoted);

    protected internal string? ExtractField(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
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
            var span = UnescapeField(buffer);
            return FetchString(span);
        }

        return FetchString(buffer);
    }

    private ReadOnlySpan<char> UnescapeField(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return Span<char>.Empty;

        var array = Pool?.Rent(value.Length) ?? new char[value.Length];
        var result = new Span<char>(array);
        int i = 0, j = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c == Profile.Descriptor.EscapeChar)
            {
                if (i + 1 == value.Length)
                    result[j++] = c;
                else if (value[i + 1] == Profile.Descriptor.EscapeChar || value[i + 1] == Profile.Descriptor.Delimiter || value[i + 1] == Profile.Descriptor.QuoteChar)
                    result[j++] = value[++i];
                else
                    result[j++] = c;
            }
            else if (c == Profile.Descriptor.QuoteChar && Profile.Descriptor.DoubleQuote)
            {
                if (value[i + 1] == Profile.Descriptor.QuoteChar)
                    result[j++] = value[++i];
                else
                    result[j++] = c;
            }
            else
                result[j++] = c;
            i++;
        };

        Pool?.Return(array);
        return result.Slice(0, j);
    }
}
