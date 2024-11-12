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
            var candidate = field.ToString();
            CheckTextQualifierEscapation(candidate, Profile.Descriptor.QuoteChar, Profile.Descriptor.EscapeChar);
            return candidate.Replace(new string(new[] { Profile.Descriptor.EscapeChar, Profile.Descriptor.QuoteChar }), Profile.Descriptor.QuoteChar.ToString());
        }
        else
            return field.ToString();
    }

    private static void CheckTextQualifierEscapation(string value, char textQualifier, char escapeTextQualifier)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (!value.Contains(textQualifier))
            return;

        var indexes = new List<int>();
        int j = -1;
        do
        {
            j = value.IndexOf(textQualifier, j + 1);
            if (j != -1)
                indexes.Add(j);
        } while (j != -1 && j < value.Length - 1);

        if (textQualifier == escapeTextQualifier)
        {
            if (indexes.Count() == 1)
                throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {indexes[0]}");

            var i = 1;
            while (i < indexes.Count())
            {
                if ((i + 1) % 2 == 0)
                {
                    if (indexes[i - 1] != indexes[i] - 1)
                        throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
                }
                else if (i == indexes.Count - 1 || indexes[i + 1] != indexes[i] + 1)
                    throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
                i += 1;
            }
        }
        else
            foreach (var index in indexes)
                if (index == 0 || value[index - 1] != escapeTextQualifier)
                    throw new ArgumentException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {index}");
    }
}
