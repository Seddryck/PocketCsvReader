using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;

public delegate string PoolString(ReadOnlySpan<char> memory);

public class FieldParser
{
    protected internal CsvProfile Profile { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    protected PoolString FetchString { get; }
    protected bool HandlesSpecialValues { get; }
    protected bool UnescapesChars { get; }

    private static readonly PoolString defaultPoolString = (ReadOnlySpan<char> span) => span.ToString();

    public FieldParser(CsvProfile profile)
        : this(profile, ArrayPool<char>.Shared) { }

    public FieldParser(CsvProfile profile, ArrayPool<char>? pool, PoolString? fetchString = null)
        => (Profile, Pool, FetchString, HandlesSpecialValues, UnescapesChars)
            = (profile, pool, profile.ParserOptimizations.PoolString ?? defaultPoolString
                , profile.ParserOptimizations.HandleSpecialValues, profile.ParserOptimizations.UnescapeChars);

    public string? ReadField(ReadOnlySpan<char> buffer, int start, int length, bool isEscapedField, bool wasQuotedField)
        => ReadField(Span<char>.Empty, buffer, start, length, isEscapedField, wasQuotedField);

    public string? ReadField(ReadOnlySpan<char> longSpan, ReadOnlySpan<char> buffer, int start, int length, bool isEscapedField, bool wasQuotedField)
    {
        ReadOnlySpan<char> fieldSpan;
        if (longSpan.Length > 0 && length>=0)
            fieldSpan = longSpan.Concat(buffer.Slice(start, length));
        else if (longSpan.Length > 0 && length < 0)
            fieldSpan = longSpan.Slice(0, longSpan.Length + length);
        else
            fieldSpan = buffer.Slice(start, length);
        return ExtractField(fieldSpan, isEscapedField, wasQuotedField);
    }

    public string? ExtractField(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
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
