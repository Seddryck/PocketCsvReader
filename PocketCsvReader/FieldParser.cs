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

    private static readonly PoolString defaultPoolString = (ReadOnlySpan<char> span) => span.ToString();

    public FieldParser(CsvProfile profile)
        : this(profile, ArrayPool<char>.Shared) { }

    public FieldParser(CsvProfile profile, ArrayPool<char>? pool, PoolString? fetchString = null)
        => (Profile, Pool, FetchString) = (profile, pool, profile.ParserOptimizations.PoolString ?? defaultPoolString);

    public string? ReadField(ReadOnlySpan<char> buffer, int start, int length, bool isEscapedField, bool wasQuotedField)
        => ReadField(Span<char>.Empty, buffer, start, length, isEscapedField, wasQuotedField);

    public string? ReadField(ReadOnlySpan<char> longSpan, ReadOnlySpan<char> buffer, int start, int length, bool isEscapedField, bool wasQuotedField)
    {
        ReadOnlySpan<char> fieldSpan;
        if (longSpan.Length > 0 && length>=0)
        {
            var newSize = longSpan.Length + length;
            var newArray = Pool?.Rent(newSize) ?? new char[newSize];
            longSpan.CopyTo(newArray);
            buffer.Slice(start, length).ToArray().CopyTo(newArray, longSpan.Length);
            fieldSpan = newArray;
            fieldSpan = fieldSpan.Slice(0, newSize);
            Pool?.Return(newArray);
        }
        else if (longSpan.Length > 0 && length < 0)
            fieldSpan = longSpan.Slice(0, longSpan.Length + length);
        else
            fieldSpan = buffer.Slice(start, length);
        return ReadField(fieldSpan, isEscapedField, wasQuotedField);
    }

    public string? ReadField(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
    {
        if (Profile.ParserOptimizations.HandleSpecialValues && buffer.Length == 0)
            return Profile.EmptyCell;
        else if (Profile.ParserOptimizations.HandleSpecialValues && !isEscapedField && !wasQuotedField)
        {
            var strField = FetchString(buffer);
            if (Profile.Sequences.TryGetValue(strField, out var value))
                return value;
            return strField;
        }

        if (Profile.ParserOptimizations.UnescapeChars && isEscapedField)
        {
            var span = UnescapeField(buffer);
            return FetchString(span);
        }
        else
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
