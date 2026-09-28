using System.Text.Json;

namespace PocketCsvReader.Ndjson;

internal sealed class JsonRecordParser
{
    private readonly string _json;
    private readonly char[] _whitespaces;
    private int _position;

    public JsonRecordParser(string json, char[] whitespaces)
    {
        _json = json;
        _whitespaces = whitespaces;
        JsonDocument.Parse(json).Dispose();
    }

    public FieldSpan[] ParseRoot()
    {
        SkipWhitespace();
        var fields = Current == '{'
            ? ParseObject()
            : [ParseValue(allowObject: false, allowCompositeArrayItems: true)];
        SkipWhitespace();
        if (!IsEnd)
            throw new InvalidDataException($"Unexpected character '{Current}' at position {_position}.");
        return fields;
    }

    private FieldSpan[] ParseObject()
    {
        Expect('{');
        SkipWhitespace();
        if (Current == '}')
        {
            _position++;
            return [];
        }

        var fields = new List<FieldSpan>();
        while (true)
        {
            var label = ParseString();
            SkipWhitespace();
            Expect(':');
            SkipWhitespace();

            var value = ParseValue(allowObject: true, allowCompositeArrayItems: true);
            fields.Add(value with { Label = label.Span, DecodedLabel = label.Decoded });

            SkipWhitespace();
            if (Current == '}')
            {
                _position++;
                return [.. fields];
            }

            Expect(',');
            SkipWhitespace();
        }
    }

    private FieldSpan ParseValue(bool allowObject, bool allowCompositeArrayItems)
    {
        if (Current == '"')
        {
            var value = ParseString();
            return new FieldSpan(value.Span, default, DecodedValue: value.Decoded);
        }

        if (Current == '{')
        {
            if (!allowObject)
                throw new InvalidDataException("Objects are not supported in this array.");

            var start = _position + 1;
            var children = ParseObject();
            return new FieldSpan(CompletedSpan(start, _position - start - 1), default, children);
        }

        if (Current == '[')
            return ParseArray(allowCompositeArrayItems);

        var scalarStart = _position;
        while (!IsEnd && Current != ',' && Current != '}' && Current != ']' && !IsJsonWhitespace(Current))
            _position++;

        if (scalarStart == _position)
            throw new InvalidDataException($"A JSON value was expected at position {_position}.");

        var scalarLength = _position - scalarStart;
        var isNull = _json.AsSpan(scalarStart, scalarLength).SequenceEqual("null");
        return new FieldSpan(CompletedSpan(scalarStart, scalarLength, isNull: isNull), default);
    }

    private FieldSpan ParseArray(bool allowCompositeItems)
    {
        Expect('[');
        var start = _position;
        SkipWhitespace();
        var children = new List<FieldSpan>();

        if (Current == ']')
        {
            _position++;
            return new FieldSpan(CompletedSpan(start, 0), default, []);
        }

        while (true)
        {
            if (!allowCompositeItems && (Current == '[' || Current == '{'))
                throw new InvalidDataException("Nested arrays and object array elements are not supported.");

            children.Add(ParseValue(allowObject: allowCompositeItems, allowCompositeArrayItems: allowCompositeItems));
            SkipWhitespace();
            if (Current == ']')
            {
                var end = _position;
                _position++;
                return new FieldSpan(CompletedSpan(start, end - start), default, [.. children]);
            }

            Expect(',');
            SkipWhitespace();
        }
    }

    private ParsedString ParseString()
    {
        var tokenStart = _position;
        Expect('"');
        var start = _position;
        var escaped = false;

        while (!IsEnd)
        {
            if (Current == '\\')
            {
                escaped = true;
                _position += 2;
                continue;
            }

            if (Current == '"')
            {
                var length = _position - start;
                _position++;
                var span = CompletedSpan(start, length, wasQuoted: true, isEscaped: escaped);
                var decoded = escaped
                    ? JsonSerializer.Deserialize<string>(_json.Substring(tokenStart, _position - tokenStart))
                    : null;
                return new ParsedString(span, decoded);
            }

            _position++;
        }

        throw new InvalidDataException("Unterminated JSON string.");
    }

    private void SkipWhitespace()
    {
        while (!IsEnd && IsJsonWhitespace(Current))
            _position++;
    }

    private bool IsJsonWhitespace(char value)
        => Array.IndexOf(_whitespaces, value) >= 0;

    private void Expect(char expected)
    {
        if (IsEnd || Current != expected)
            throw new InvalidDataException($"Expected '{expected}' at position {_position}.");
        _position++;
    }

    private char Current => IsEnd ? '\0' : _json[_position];
    private bool IsEnd => _position >= _json.Length;

    private static SpanInfo CompletedSpan(int start, int length, bool wasQuoted = false, bool isEscaped = false, bool isNull = false)
        => new(start, length, wasQuoted, isEscaped, IsStarted: true, IsComplete: true, IsNull: isNull);

    private readonly record struct ParsedString(SpanInfo Span, string? Decoded);
}
