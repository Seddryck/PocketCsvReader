using System.Globalization;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal static class JsonValues
{
    public static bool IsNull(ReadOnlySpan<char> value) => value.SequenceEqual("null");

    public static bool NameEquals(ReadOnlySpan<char> quoted, string name, JsonFieldBuffer scratch)
        => Text(quoted, scratch).SequenceEqual(name);

    public static ReadOnlySpan<char> Text(ReadOnlySpan<char> quoted, JsonFieldBuffer scratch)
    {
        if (quoted.IsEmpty || quoted[0] != '"')
            throw new InvalidDataException("Expected a JSON string.");
        var text = quoted[1..^1];
        return text.Contains('\\') ? JsonRecordParser.DecodeString(quoted, scratch).AsSpan() : text;
    }

    public static ReadOnlySpan<char> Number(ReadOnlySpan<char> value, JsonFieldBuffer scratch, bool allowQuoted)
    {
        if (allowQuoted && !value.IsEmpty && value[0] == '"')
            value = Text(value, scratch);
        if (value.IsEmpty || value[0] is not ('-' or >= '0' and <= '9'))
            throw new InvalidDataException("Expected a JSON number.");
        return value;
    }

    public static int Count(ReadOnlySpan<char> value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            throw new InvalidDataException("Expected a non-negative JSON integer.");
        return count;
    }

    public static int Integer(ReadOnlySpan<char> value)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException("Expected a JSON integer.");
        return number;
    }

    public static bool Boolean(ReadOnlySpan<char> value)
    {
        if (value.SequenceEqual("true"))
            return true;
        if (value.SequenceEqual("false"))
            return false;
        throw new InvalidDataException("Expected a JSON boolean.");
    }

    public static ReadOnlySpan<char> Property(
        ReadOnlySpan<char> json, string name, JsonFieldBuffer scratch, bool required = true)
    {
        var result = ReadOnlySpan<char>.Empty;
        var properties = JsonRecordParser.Enumerate(json, properties: true);
        while (properties.MoveNext())
        {
            if (!NameEquals(properties.Name, name, scratch))
                continue;
            if (!result.IsEmpty)
                throw new InvalidDataException($"Duplicate JSON property '{name}'.");
            result = properties.Value;
        }

        if (required && result.IsEmpty)
            throw new InvalidDataException($"Missing JSON property '{name}'.");
        return result;
    }
}
