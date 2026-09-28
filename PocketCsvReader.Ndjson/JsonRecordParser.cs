using System.Text.Json;

namespace PocketCsvReader.Ndjson;

internal sealed class JsonRecordParser
{
    private readonly string _json;
    private int _position;

    public JsonRecordParser(string json)
    {
        _json = json;
        JsonDocument.Parse(json).Dispose();
    }

    public FieldSpan[] ParseRoot()
    {
        SkipSpaces();
        var fields = Current == '{'
            ? ParseObject()
            : [ParseValue(allowObject: false, allowCompositeArrayItems: true)];
        SkipSpaces();
        if (!IsEnd)
            throw new InvalidDataException($"Unexpected character '{Current}' at position {_position}.");
        return fields;
    }

    private FieldSpan[] ParseObject()
    {
        Expect('{');
        SkipSpaces();
        if (Current == '}')
        {
            _position++;
            return [];
        }

        var fields = new List<FieldSpan>();
        while (true)
        {
            var label = ParseString();
            SkipSpaces();
            Expect(':');
            SkipSpaces();

            var value = ParseValue(allowObject: true, allowCompositeArrayItems: true);
            fields.Add(value with { Label = label });

            SkipSpaces();
            if (Current == '}')
            {
                _position++;
                return [.. fields];
            }

            Expect(',');
            SkipSpaces();
        }
    }

    private FieldSpan ParseValue(bool allowObject, bool allowCompositeArrayItems)
    {
        if (Current == '"')
            return new FieldSpan(ParseString(), default);

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
        while (!IsEnd && Current != ',' && Current != '}' && Current != ']' && Current != ' ')
            _position++;

        if (scalarStart == _position)
            throw new InvalidDataException($"A JSON value was expected at position {_position}.");

        return new FieldSpan(CompletedSpan(scalarStart, _position - scalarStart), default);
    }

    private FieldSpan ParseArray(bool allowCompositeItems)
    {
        Expect('[');
        var start = _position;
        SkipSpaces();
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
            SkipSpaces();
            if (Current == ']')
            {
                var end = _position;
                _position++;
                return new FieldSpan(CompletedSpan(start, end - start), default, [.. children]);
            }

            Expect(',');
            SkipSpaces();
        }
    }

    private SpanInfo ParseString()
    {
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
                return CompletedSpan(start, length, wasQuoted: true, isEscaped: escaped);
            }

            _position++;
        }

        throw new InvalidDataException("Unterminated JSON string.");
    }

    private void SkipSpaces()
    {
        while (!IsEnd && Current == ' ')
            _position++;
    }

    private void Expect(char expected)
    {
        if (IsEnd || Current != expected)
            throw new InvalidDataException($"Expected '{expected}' at position {_position}.");
        _position++;
    }

    private char Current => IsEnd ? '\0' : _json[_position];
    private bool IsEnd => _position >= _json.Length;

    private static SpanInfo CompletedSpan(int start, int length, bool wasQuoted = false, bool isEscaped = false)
        => new(start, length, wasQuoted, isEscaped, IsStarted: true, IsComplete: true);
}
