using System.Text.Json;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

internal static class KeyValueRecordParser
{
    public static FieldSpan[] Parse(string record, KeyValueFormat format)
        => format switch
        {
            KeyValueFormat.Ltsv => ParseLtsv(record),
            KeyValueFormat.Logfmt => ParseLogfmt(record),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };

    private static FieldSpan[] ParseLtsv(string record)
    {
        if (record.Length == 0)
            return [];

        var fields = new List<FieldSpan>();
        var fieldStart = 0;
        while (fieldStart <= record.Length)
        {
            var fieldEnd = record.IndexOf('\t', fieldStart);
            if (fieldEnd < 0)
                fieldEnd = record.Length;

            var separator = record.IndexOf(':', fieldStart, fieldEnd - fieldStart);
            if (separator < 0)
                throw SyntaxError("An LTSV field must contain ':'", fieldStart);
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
        return [.. fields];
    }

    private static FieldSpan[] ParseLogfmt(string record)
    {
        var fields = new List<FieldSpan>();
        var position = 0;
        while (true)
        {
            SkipWhitespace(record, ref position);
            if (position == record.Length)
                return [.. fields];

            var labelStart = position;
            while (position < record.Length && IsIdentifierChar(record[position]))
                position++;
            if (labelStart == position)
                throw SyntaxError("A logfmt key was expected", position);

            var label = new SpanInfo(labelStart, position - labelStart);
            if (position == record.Length || IsWhitespace(record[position]))
            {
                fields.Add(new FieldSpan(new SpanInfo(position, 0), label));
                continue;
            }

            if (record[position] != '=')
                throw SyntaxError("A logfmt key must be followed by '=' or whitespace", position);
            position++;

            if (position == record.Length || IsWhitespace(record[position]))
            {
                fields.Add(new FieldSpan(new SpanInfo(position, 0), label));
                continue;
            }

            if (record[position] == '"')
            {
                fields.Add(ParseQuotedLogfmtValue(record, label, ref position));
                continue;
            }

            var valueStart = position;
            while (position < record.Length && IsIdentifierChar(record[position]))
                position++;
            if (valueStart == position)
                throw SyntaxError("A logfmt value was expected", position);
            if (position < record.Length && !IsWhitespace(record[position]))
                throw SyntaxError("An invalid character was found in a logfmt value", position);

            fields.Add(new FieldSpan(new SpanInfo(valueStart, position - valueStart), label));
        }
    }

    private static FieldSpan ParseQuotedLogfmtValue(string record, SpanInfo label, ref int position)
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
                        decoded = JsonSerializer.Deserialize<string>(record.Substring(quoteStart, position - quoteStart));
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

    private static void SkipWhitespace(string record, ref int position)
    {
        while (position < record.Length && IsWhitespace(record[position]))
            position++;
    }

    private static void ValidateLtsvLabel(string record, int start, int length)
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

    private static void ValidateLtsvValue(string record, int start, int length)
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
