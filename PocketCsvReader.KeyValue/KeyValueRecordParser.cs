using System.Text.Json;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

internal static class KeyValueRecordParser
{
    public static void Parse(ReadOnlySpan<char> record, KeyValueFormat format, List<FieldSpan> fields)
    {
        fields.Clear();
        switch (format)
        {
            case KeyValueFormat.Ltsv: ParseLtsv(record, fields); break;
            case KeyValueFormat.Logfmt: ParseLogfmt(record, fields); break;
            default: throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }

    private static void ParseLtsv(ReadOnlySpan<char> record, List<FieldSpan> fields)
    {
        if (record.Length == 0)
            return;

        var fieldStart = 0;
        while (fieldStart <= record.Length)
        {
            var fieldEnd = record[fieldStart..].IndexOf('\t');
            if (fieldEnd < 0)
                fieldEnd = record.Length;
            else
                fieldEnd += fieldStart;

            var separator = record.Slice(fieldStart, fieldEnd - fieldStart).IndexOf(':');
            if (separator < 0)
                throw SyntaxError("An LTSV field must contain ':'", fieldStart);
            separator += fieldStart;
            if (separator == fieldStart)
                throw SyntaxError("An LTSV label cannot be empty", fieldStart);

            ValidateLtsvLabel(record, fieldStart, separator - fieldStart);
            ValidateLtsvValue(record, separator + 1, fieldEnd - separator - 1);

            fields.Add(new FieldSpan(
                new SpanInfo(separator + 1, fieldEnd - separator - 1),
                new SpanInfo(fieldStart, separator - fieldStart)));

            if (fieldEnd == record.Length)
                break;
            fieldStart = fieldEnd + 1;
        }
    }

    private static void ParseLogfmt(ReadOnlySpan<char> record, List<FieldSpan> fields)
    {
        var position = 0;
        while (true)
        {
            SkipWhitespace(record, ref position);
            if (position == record.Length)
                return;

            fields.Add(ParseLogfmtField(record, ref position));
        }
    }

    private static FieldSpan ParseLogfmtField(ReadOnlySpan<char> record, ref int position)
    {
        var labelStart = position;
        while (position < record.Length && IsIdentifierChar(record[position]))
            position++;
        if (labelStart == position)
            throw SyntaxError("A logfmt key was expected", position);

        var label = new SpanInfo(labelStart, position - labelStart);
        if (position == record.Length || IsWhitespace(record[position]))
            return new FieldSpan(new SpanInfo(position, 0), label);

        if (record[position] != '=')
            throw SyntaxError("A logfmt key must be followed by '=' or whitespace", position);
        position++;
        return ParseLogfmtValue(record, label, ref position);
    }

    private static FieldSpan ParseLogfmtValue(ReadOnlySpan<char> record, SpanInfo label, ref int position)
    {
        if (position == record.Length || IsWhitespace(record[position]))
            return new FieldSpan(new SpanInfo(position, 0), label);

        if (record[position] == '"')
            return ParseQuotedLogfmtValue(record, label, ref position);

        var valueStart = position;
        while (position < record.Length && IsIdentifierChar(record[position]))
            position++;
        if (valueStart == position)
            throw SyntaxError("A logfmt value was expected", position);
        if (position < record.Length && !IsWhitespace(record[position]))
            throw SyntaxError("An invalid character was found in a logfmt value", position);

        return new FieldSpan(new SpanInfo(valueStart, position - valueStart), label);
    }

    private static FieldSpan ParseQuotedLogfmtValue(ReadOnlySpan<char> record, SpanInfo label, ref int position)
    {
        var quoteStart = position++;
        var valueStart = position;
        var escaped = false;
        while (position < record.Length)
        {
            var current = record[position];
            if (current == '\\')
            {
                escaped = true;
                position += 2;
                if (position > record.Length)
                    throw SyntaxError("A logfmt escape sequence is incomplete", record.Length - 1);
                continue;
            }

            if (current == '"')
            {
                var valueLength = position - valueStart;
                position++;
                if (position < record.Length && !IsWhitespace(record[position]))
                    throw SyntaxError("A quoted logfmt value must be followed by whitespace", position);

                string? decoded = null;
                if (escaped)
                {
                    try
                    {
                        decoded = JsonSerializer.Deserialize<string>(record.Slice(quoteStart, position - quoteStart));
                    }
                    catch (JsonException exception)
                    {
                        throw SyntaxError("The quoted logfmt value contains an invalid escape sequence", quoteStart, exception);
                    }
                }

                return new FieldSpan(
                    new SpanInfo(valueStart, valueLength, WasQuoted: true, IsEscaped: escaped),
                    label,
                    DecodedValue: decoded);
            }

            if (current < ' ')
                throw SyntaxError("A quoted logfmt value contains an unescaped control character", position);
            position++;
        }

        throw SyntaxError("A quoted logfmt value is unterminated", quoteStart);
    }

    private static bool IsIdentifierChar(char value)
        => value > ' ' && value is not ('=' or '"');

    private static bool IsWhitespace(char value)
        => value <= ' ';

    private static void SkipWhitespace(ReadOnlySpan<char> record, ref int position)
    {
        while (position < record.Length && IsWhitespace(record[position]))
            position++;
    }

    private static void ValidateLtsvLabel(ReadOnlySpan<char> record, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            var value = record[index];
            if (!((value >= '0' && value <= '9')
                || (value >= 'A' && value <= 'Z')
                || (value >= 'a' && value <= 'z')
                || value is '_' or '.' or '-'))
            {
                throw SyntaxError("An LTSV label contains an invalid character", index);
            }
        }
    }

    private static void ValidateLtsvValue(ReadOnlySpan<char> record, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (record[index] is '\0' or '\r' or '\n')
                throw SyntaxError("An LTSV value contains an invalid character", index);
        }
    }

    private static InvalidDataException SyntaxError(string message, int position, Exception? inner = null)
        => new($"{message} at position {position}.", inner);
}
