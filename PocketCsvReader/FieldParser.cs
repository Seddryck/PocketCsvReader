using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;
public class FieldParser
{
    protected internal CsvProfile Profile { get; private set; }

    public FieldParser(CsvProfile profile)
        => Profile = profile;

    public string? ReadField(Span<char> longField, int longFieldIndex, ReadOnlySpan<char> buffer, int currentIndex, bool isFieldWithTextQualifier, bool isFieldEndingByTextQualifier)
    {
        if (longField.Length > longFieldIndex + currentIndex)
        {
            buffer.Slice(0, currentIndex + 1).CopyTo(longField.Slice(longFieldIndex));
        }
        else
        {
            var newArray = ArrayPool<char>.Shared.Rent(longFieldIndex + currentIndex);
            longField.CopyTo(newArray);
            buffer.Slice(0, currentIndex).ToArray().CopyTo(newArray, longFieldIndex);
            longField = newArray;
        }
        return ReadField(longField, 0, longFieldIndex + currentIndex, isFieldWithTextQualifier, isFieldEndingByTextQualifier);
    }

    public string? ReadField(ReadOnlySpan<char> buffer, int indexFieldStart, int currentIndex, bool isFieldWithTextQualifier, bool isFieldEndingByTextQualifier)
    {
        if (isFieldWithTextQualifier != isFieldEndingByTextQualifier)
            if (isFieldWithTextQualifier)
                throw new InvalidDataException($"the token {buffer.Slice(indexFieldStart, currentIndex - indexFieldStart)} is starting by a text-qualifier but not ending by a text-qualifier.");
            else
                throw new InvalidDataException($"the token {buffer.Slice(indexFieldStart, currentIndex - indexFieldStart)} is ending by a text-qualifier but not starting by a text-qualifier.");

        var field = isFieldWithTextQualifier
                        ? buffer.Slice(indexFieldStart + 1, currentIndex - indexFieldStart - 2)
                        : buffer.Slice(indexFieldStart, currentIndex - indexFieldStart);

        if (Profile.ParserOptimizations.HandleSpecialValues && field.Length == 0)
            return Profile.EmptyCell;
        else if (Profile.ParserOptimizations.HandleSpecialValues && field.ToString() == "(null)" && !isFieldWithTextQualifier)
            return null;
        else if (Profile.ParserOptimizations.UnescapeChars && field.Contains(Profile.Descriptor.EscapeChar))
        {
            var result = UnescapeTextQualifier(field, Profile.Descriptor.QuoteChar, Profile.Descriptor.EscapeChar);
            return result.ToString();
        }
        else
            return field.ToString();
    }

    private static ReadOnlySpan<char> UnescapeTextQualifier(ReadOnlySpan<char> value, char textQualifier, char escapeTextQualifier)
    {
        if (value.Length==0)
            return Span<char>.Empty;

        var result = new Span<char>(new char[value.Length]);
        int i =0, j = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c == escapeTextQualifier)
            {
                if (i+1 == value.Length)
                    throw new InvalidDataException($"the token {value.ToString()} contains an escape-text-qualifier at the last position '{i + 1}'");
                else if (value[i+1] != textQualifier)
                    throw new InvalidDataException($"the token {value.ToString()} contains a text-qualifier not preceded by a an escape-text-qualifier at the position '{j}'");
                result[j++] = textQualifier;
                i+=2;
            }
            else
            {
                result[j++] = c;
                i += 1;
            }
        } ;

        return result.Slice(0, j);
    }
}
