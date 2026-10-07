using System.Text;

namespace PocketCsvReader.Json;

internal ref struct JsonRecordParser
{
    private readonly ReadOnlySpan<char> _json;
    private readonly char[] _whitespaces;
    private int _position;

    private JsonRecordParser(ReadOnlySpan<char> json, char[] whitespaces)
    {
        _json = json;
        _whitespaces = whitespaces;
    }

    public static FieldSpan[] Parse(ReadOnlySpan<char> json, char[] whitespaces)
    {
        var parser = new JsonRecordParser(json, whitespaces);
        return parser.ParseRoot();
    }

    private FieldSpan[] ParseRoot()
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

            var start = _position;
            var children = ParseObject();
            return new FieldSpan(CompletedSpan(start, _position - start), default, children);
        }

        if (Current == '[')
            return ParseArray(allowCompositeArrayItems);

        return Current switch
        {
            't' => ParseLiteral("true"),
            'f' => ParseLiteral("false"),
            'n' => ParseLiteral("null", isNull: true),
            '-' => ParseNumber(),
            >= '0' and <= '9' => ParseNumber(),
            _ => throw new InvalidDataException($"A JSON value was expected at position {_position}.")
        };
    }

    private FieldSpan ParseLiteral(string literal, bool isNull = false)
    {
        var start = _position;
        if (_json.Length - start < literal.Length
            || !_json.Slice(start, literal.Length).SequenceEqual(literal))
        {
            throw new InvalidDataException($"Invalid JSON literal at position {start}.");
        }

        _position += literal.Length;
        return new FieldSpan(CompletedSpan(start, literal.Length, isNull: isNull), default);
    }

    private FieldSpan ParseNumber()
    {
        var start = _position;
        if (Current == '-')
            _position++;

        ParseIntegerPart();
        ParseFraction();
        ParseExponent();

        return new FieldSpan(CompletedSpan(start, _position - start), default);
    }

    private void ParseIntegerPart()
    {
        if (Current == '0')
        {
            _position++;
            if (IsDigit(Current))
                throw new InvalidDataException($"A JSON number cannot contain a leading zero at position {_position}.");
            return;
        }

        if (!IsNonZeroDigit(Current))
            throw new InvalidDataException($"A digit was expected at position {_position}.");
        SkipDigits();
    }

    private void ParseFraction()
    {
        if (Current != '.')
            return;
        _position++;
        ParseRequiredDigits("fractional");
    }

    private void ParseExponent()
    {
        if (Current is not ('e' or 'E'))
            return;
        _position++;
        if (Current is '+' or '-')
            _position++;
        ParseRequiredDigits("exponent");
    }

    private void ParseRequiredDigits(string component)
    {
        if (!IsDigit(Current))
            throw new InvalidDataException($"A {component} digit was expected at position {_position}.");
        SkipDigits();
    }

    private void SkipDigits()
    {
        while (IsDigit(Current))
            _position++;
    }

    private FieldSpan ParseArray(bool allowCompositeItems)
    {
        var start = _position;
        Expect('[');
        SkipWhitespace();
        var children = new List<FieldSpan>();

        if (Current == ']')
        {
            _position++;
            return new FieldSpan(CompletedSpan(start, _position - start), default, []);
        }

        while (true)
        {
            if (!allowCompositeItems && (Current == '[' || Current == '{'))
                throw new InvalidDataException("Nested arrays and object array elements are not supported.");

            children.Add(ParseValue(allowObject: allowCompositeItems, allowCompositeArrayItems: allowCompositeItems));
            SkipWhitespace();
            if (Current == ']')
            {
                _position++;
                return new FieldSpan(CompletedSpan(start, _position - start), default, [.. children]);
            }

            Expect(',');
            SkipWhitespace();
        }
    }

    private ParsedString ParseString()
    {
        Expect('"');
        var start = _position;
        var segmentStart = start;
        StringBuilder? decoded = null;

        while (!IsEnd)
        {
            if (Current == '\\')
            {
                decoded ??= new StringBuilder();
                decoded.Append(_json.Slice(segmentStart, _position - segmentStart));
                _position++;
                DecodeEscape(decoded);
                segmentStart = _position;
                continue;
            }

            if (Current == '"')
            {
                var length = _position - start;
                decoded?.Append(_json.Slice(segmentStart, _position - segmentStart));
                _position++;
                var span = CompletedSpan(start, length, wasQuoted: true, isEscaped: decoded is not null);
                return new ParsedString(span, decoded?.ToString());
            }

            if (Current < ' ')
                throw new InvalidDataException($"Unescaped control character at position {_position}.");
            _position++;
        }

        throw new InvalidDataException("Unterminated JSON string.");
    }

    private void DecodeEscape(StringBuilder decoded)
    {
        if (IsEnd)
            throw new InvalidDataException("Incomplete JSON escape sequence.");

        switch (Current)
        {
            case '"': decoded.Append('"'); _position++; break;
            case '\\': decoded.Append('\\'); _position++; break;
            case '/': decoded.Append('/'); _position++; break;
            case 'b': decoded.Append('\b'); _position++; break;
            case 'f': decoded.Append('\f'); _position++; break;
            case 'n': decoded.Append('\n'); _position++; break;
            case 'r': decoded.Append('\r'); _position++; break;
            case 't': decoded.Append('\t'); _position++; break;
            case 'u': DecodeUnicodeEscape(decoded); break;
            default:
                throw new InvalidDataException($"Invalid JSON escape character '{Current}' at position {_position}.");
        }
    }

    private void DecodeUnicodeEscape(StringBuilder decoded)
    {
        var codeUnit = ParseHexQuad(_position + 1);
        _position += 5;

        if (char.IsLowSurrogate(codeUnit))
            throw new InvalidDataException("A low surrogate must follow a high surrogate.");

        if (!char.IsHighSurrogate(codeUnit))
        {
            decoded.Append(codeUnit);
            return;
        }

        if (_position + 6 > _json.Length
            || _json[_position] != '\\'
            || _json[_position + 1] != 'u')
        {
            throw new InvalidDataException("A high surrogate must be followed by a Unicode low-surrogate escape.");
        }

        var lowSurrogate = ParseHexQuad(_position + 2);
        if (!char.IsLowSurrogate(lowSurrogate))
            throw new InvalidDataException("A high surrogate must be followed by a low surrogate.");

        decoded.Append(codeUnit);
        decoded.Append(lowSurrogate);
        _position += 6;
    }

    private char ParseHexQuad(int start)
    {
        if (start + 4 > _json.Length)
            throw new InvalidDataException($"Incomplete Unicode escape sequence at position {start - 1}.");

        var value = 0;
        for (var index = start; index < start + 4; index++)
        {
            var digit = HexValue(_json[index]);
            if (digit < 0)
                throw new InvalidDataException($"Invalid hexadecimal digit at position {index}.");
            value = (value << 4) | digit;
        }
        return (char)value;
    }

    private static int HexValue(char value)
        => value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1
        };

    private static bool IsDigit(char value)
        => value is >= '0' and <= '9';

    private static bool IsNonZeroDigit(char value)
        => value is >= '1' and <= '9';

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
