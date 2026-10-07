namespace PocketCsvReader.Json;

internal sealed class ProjectedJsonValueFramer : IJsonValueFramer
{
    private readonly IReadOnlyList<string> _projectedProperties;
    private readonly FieldSpan[] _fields;
    private readonly bool[] _propertyCandidates;
    private ContainerFrame[] _stack = new ContainerFrame[16];
    private int _depth;
    private int _position;
    private long _startPosition;
    private TokenKind _token;
    private int _tokenStart;
    private int _tokenOrdinal;
    private SpanInfo _tokenLabel;
    private string? _tokenDecodedLabel;
    private string _literal = string.Empty;
    private int _literalIndex;
    private NumberState _numberState;
    private bool _stringIsProperty;
    private bool _stringWasEscaped;
    private StringEscapeState _stringEscapeState;
    private int _unicodeDigits;
    private int _unicodeValue;
    private char _highSurrogate;
    private int _propertyLength;
    private bool _complete;

    public int Delimiter { get; private set; }
    public bool RequiresValueMaterialization { get; private set; }

    public ProjectedJsonValueFramer(IReadOnlyList<string> projectedProperties, FieldSpan[] fields)
    {
        _projectedProperties = projectedProperties;
        _fields = fields;
        _propertyCandidates = new bool[projectedProperties.Count];
    }

    public void Prepare(char first, long startPosition)
        => _startPosition = startPosition;

    public void Reset()
    {
        Array.Clear(_fields);
        _depth = 0;
        _position = 0;
        _token = TokenKind.None;
        _complete = false;
        Delimiter = -1;
        RequiresValueMaterialization = false;
    }

    public FrameScanResult Scan(ReadOnlySpan<char> input)
    {
        var index = 0;
        while (index < input.Length)
        {
            if (!Process(input[index]))
                continue;

            index++;
            _position++;
            if (_complete)
                return new FrameScanResult(index, true, _position);
        }

        return new FrameScanResult(input.Length, false);
    }

    public FrameScanResult CompleteAtEof()
        => throw Error("The JSON value is incomplete");

    private bool Process(char current)
    {
        if (_token != TokenKind.None)
            return ProcessToken(current);

        if (_depth == 0)
        {
            if (_position != 0 || current != '{')
                throw Error("A projected JSON value must be an object");
            Push(ContainerKind.Object, _position, -1, default, null);
            return true;
        }

        if (IsWhitespace(current))
            return true;

        ref var frame = ref _stack[_depth - 1];
        return frame.Kind == ContainerKind.Object
            ? ProcessObjectCharacter(ref frame, current)
            : ProcessArrayCharacter(ref frame, current);
    }

    private bool ProcessObjectCharacter(ref ContainerFrame frame, char current)
    {
        switch (frame.State)
        {
            case ContainerState.PropertyOrEnd:
                if (current == '}')
                    return CloseContainer(ContainerKind.Object);
                if (current != '"')
                    throw Error("A JSON property name was expected");
                BeginString(isProperty: true, ordinal: -1, default, null);
                return true;

            case ContainerState.Property:
                if (current != '"')
                    throw Error("A JSON property name was expected");
                BeginString(isProperty: true, ordinal: -1, default, null);
                return true;

            case ContainerState.Colon:
                if (current != ':')
                    throw Error("Expected ':'");
                frame.State = ContainerState.Value;
                return true;

            case ContainerState.Value:
                return BeginValue(ref frame, current);

            case ContainerState.CommaOrEnd:
                if (current == ',')
                {
                    frame.State = ContainerState.Property;
                    return true;
                }
                if (current == '}')
                    return CloseContainer(ContainerKind.Object);
                throw Error("Expected ',' or '}'");

            default:
                throw new InvalidOperationException("Invalid JSON object parser state.");
        }
    }

    private bool ProcessArrayCharacter(ref ContainerFrame frame, char current)
    {
        switch (frame.State)
        {
            case ContainerState.ValueOrEnd:
                if (current == ']')
                    return CloseContainer(ContainerKind.Array);
                return BeginValue(ref frame, current);

            case ContainerState.Value:
                return BeginValue(ref frame, current);

            case ContainerState.CommaOrEnd:
                if (current == ',')
                {
                    frame.State = ContainerState.Value;
                    return true;
                }
                if (current == ']')
                    return CloseContainer(ContainerKind.Array);
                throw Error("Expected ',' or ']'");

            default:
                throw new InvalidOperationException("Invalid JSON array parser state.");
        }
    }

    private bool BeginValue(ref ContainerFrame frame, char current)
    {
        var ordinal = frame.Kind == ContainerKind.Object && _depth == 1
            ? frame.ProjectedOrdinal
            : -1;
        if (ordinal >= 0 && _fields[ordinal].Value.IsStarted)
            ordinal = -1;

        var label = frame.Label;
        var decodedLabel = frame.DecodedLabel;
        switch (current)
        {
            case '"':
                BeginString(isProperty: false, ordinal, label, decodedLabel);
                return true;
            case '{':
                if (ordinal >= 0)
                    RequiresValueMaterialization = true;
                Push(ContainerKind.Object, _position, ordinal, label, decodedLabel);
                return true;
            case '[':
                if (ordinal >= 0)
                    RequiresValueMaterialization = true;
                Push(ContainerKind.Array, _position, ordinal, label, decodedLabel);
                return true;
            case 't':
                BeginLiteral("true", ordinal, label, decodedLabel);
                return true;
            case 'f':
                BeginLiteral("false", ordinal, label, decodedLabel);
                return true;
            case 'n':
                BeginLiteral("null", ordinal, label, decodedLabel);
                return true;
            case '-':
                BeginNumber(NumberState.AfterMinus, ordinal, label, decodedLabel);
                return true;
            case >= '0' and <= '9':
                BeginNumber(current == '0' ? NumberState.Zero : NumberState.Integer, ordinal, label, decodedLabel);
                return true;
            default:
                throw Error("A JSON value was expected");
        }
    }

    private bool ProcessToken(char current)
        => _token switch
        {
            TokenKind.String => ProcessString(current),
            TokenKind.Literal => ProcessLiteral(current),
            TokenKind.Number => ProcessNumber(current),
            _ => throw new InvalidOperationException("Invalid JSON token parser state.")
        };

    private void BeginString(bool isProperty, int ordinal, SpanInfo label, string? decodedLabel)
    {
        _token = TokenKind.String;
        _tokenStart = _position + 1;
        _tokenOrdinal = ordinal;
        _tokenLabel = label;
        _tokenDecodedLabel = decodedLabel;
        _stringIsProperty = isProperty;
        _stringWasEscaped = false;
        _stringEscapeState = StringEscapeState.None;
        _highSurrogate = '\0';

        if (isProperty && _depth == 1)
        {
            Array.Fill(_propertyCandidates, true);
            _propertyLength = 0;
        }
    }

    private bool ProcessString(char current)
    {
        if (_stringEscapeState != StringEscapeState.None)
            return ProcessStringEscape(current);

        if (current == '\\')
        {
            _stringWasEscaped = true;
            if (!_stringIsProperty && _tokenOrdinal >= 0)
                RequiresValueMaterialization = true;
            _stringEscapeState = StringEscapeState.AfterSlash;
            return true;
        }

        if (current == '"')
        {
            if (_stringIsProperty)
                CompletePropertyName();
            else
                CompleteStringValue();
            _token = TokenKind.None;
            return true;
        }

        if (current < ' ')
            throw Error("An unescaped control character is not allowed in a JSON string");

        MatchPropertyCharacter(current);
        return true;
    }

    private bool ProcessStringEscape(char current)
    {
        switch (_stringEscapeState)
        {
            case StringEscapeState.AfterSlash:
                if (current == 'u')
                {
                    _unicodeDigits = 0;
                    _unicodeValue = 0;
                    _stringEscapeState = StringEscapeState.Unicode;
                    return true;
                }

                var decoded = current switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw Error($"Invalid JSON escape character '{current}'")
                };
                MatchPropertyCharacter(decoded);
                _stringEscapeState = StringEscapeState.None;
                return true;

            case StringEscapeState.Unicode:
                var digit = HexValue(current);
                if (digit < 0)
                    throw Error("Invalid hexadecimal digit in a Unicode escape sequence");
                _unicodeValue = (_unicodeValue << 4) | digit;
                if (++_unicodeDigits < 4)
                    return true;
                CompleteUnicodeCodeUnit((char)_unicodeValue);
                return true;

            case StringEscapeState.LowSurrogateSlash:
                if (current != '\\')
                    throw Error("A high surrogate must be followed by a Unicode low-surrogate escape");
                _stringEscapeState = StringEscapeState.LowSurrogateU;
                return true;

            case StringEscapeState.LowSurrogateU:
                if (current != 'u')
                    throw Error("A high surrogate must be followed by a Unicode low-surrogate escape");
                _unicodeDigits = 0;
                _unicodeValue = 0;
                _stringEscapeState = StringEscapeState.Unicode;
                return true;

            default:
                throw new InvalidOperationException("Invalid JSON string escape state.");
        }
    }

    private void CompleteUnicodeCodeUnit(char codeUnit)
    {
        if (_highSurrogate != '\0')
        {
            if (!char.IsLowSurrogate(codeUnit))
                throw Error("A high surrogate must be followed by a low surrogate");
            MatchPropertyCharacter(_highSurrogate);
            MatchPropertyCharacter(codeUnit);
            _highSurrogate = '\0';
            _stringEscapeState = StringEscapeState.None;
            return;
        }

        if (char.IsLowSurrogate(codeUnit))
            throw Error("A low surrogate must follow a high surrogate");
        if (char.IsHighSurrogate(codeUnit))
        {
            _highSurrogate = codeUnit;
            _stringEscapeState = StringEscapeState.LowSurrogateSlash;
            return;
        }

        MatchPropertyCharacter(codeUnit);
        _stringEscapeState = StringEscapeState.None;
    }

    private void MatchPropertyCharacter(char value)
    {
        if (!_stringIsProperty || _depth != 1)
            return;

        for (var ordinal = 0; ordinal < _propertyCandidates.Length; ordinal++)
        {
            if (_propertyCandidates[ordinal]
                && (_propertyLength >= _projectedProperties[ordinal].Length
                    || _projectedProperties[ordinal][_propertyLength] != value))
            {
                _propertyCandidates[ordinal] = false;
            }
        }
        _propertyLength++;
    }

    private void CompletePropertyName()
    {
        ref var frame = ref _stack[_depth - 1];
        var ordinal = -1;
        if (_depth == 1)
        {
            for (var candidate = 0; candidate < _propertyCandidates.Length; candidate++)
            {
                if (_propertyCandidates[candidate]
                    && _projectedProperties[candidate].Length == _propertyLength)
                {
                    ordinal = candidate;
                    break;
                }
            }
        }

        frame.ProjectedOrdinal = ordinal;
        frame.Label = CompletedSpan(
            _tokenStart,
            _position - _tokenStart,
            wasQuoted: true,
            isEscaped: _stringWasEscaped);
        frame.DecodedLabel = ordinal >= 0 && _stringWasEscaped
            ? _projectedProperties[ordinal]
            : null;
        frame.State = ContainerState.Colon;
    }

    private void CompleteStringValue()
    {
        var value = CompletedSpan(
            _tokenStart,
            _position - _tokenStart,
            wasQuoted: true,
            isEscaped: _stringWasEscaped);
        SetField(value, isNull: false);
        CompleteParentValue();
    }

    private void BeginLiteral(string literal, int ordinal, SpanInfo label, string? decodedLabel)
    {
        BeginPrimitive(TokenKind.Literal, ordinal, label, decodedLabel);
        _literal = literal;
        _literalIndex = 1;
    }

    private bool ProcessLiteral(char current)
    {
        if (_literalIndex < _literal.Length)
        {
            if (current != _literal[_literalIndex++])
                throw Error("Invalid JSON literal");
            return true;
        }

        if (!IsValueDelimiter(current))
            throw Error("Invalid character after a JSON literal");
        CompletePrimitive(isNull: _literal == "null");
        return false;
    }

    private void BeginNumber(
        NumberState state,
        int ordinal,
        SpanInfo label,
        string? decodedLabel)
    {
        BeginPrimitive(TokenKind.Number, ordinal, label, decodedLabel);
        _numberState = state;
    }

    private bool ProcessNumber(char current)
    {
        switch (_numberState)
        {
            case NumberState.AfterMinus:
                if (current == '0')
                    _numberState = NumberState.Zero;
                else if (current is >= '1' and <= '9')
                    _numberState = NumberState.Integer;
                else
                    throw Error("A digit was expected after the JSON number sign");
                return true;

            case NumberState.Zero:
                if (current == '.')
                {
                    _numberState = NumberState.Dot;
                    return true;
                }
                if (current is 'e' or 'E')
                {
                    _numberState = NumberState.ExponentMark;
                    return true;
                }
                if (current is >= '0' and <= '9')
                    throw Error("A JSON number cannot contain a leading zero");
                return CompleteNumberAtDelimiter(current);

            case NumberState.Integer:
                if (current is >= '0' and <= '9')
                    return true;
                if (current == '.')
                {
                    _numberState = NumberState.Dot;
                    return true;
                }
                if (current is 'e' or 'E')
                {
                    _numberState = NumberState.ExponentMark;
                    return true;
                }
                return CompleteNumberAtDelimiter(current);

            case NumberState.Dot:
                if (current is not (>= '0' and <= '9'))
                    throw Error("A fractional digit was expected");
                _numberState = NumberState.Fraction;
                return true;

            case NumberState.Fraction:
                if (current is >= '0' and <= '9')
                    return true;
                if (current is 'e' or 'E')
                {
                    _numberState = NumberState.ExponentMark;
                    return true;
                }
                return CompleteNumberAtDelimiter(current);

            case NumberState.ExponentMark:
                if (current is '+' or '-')
                    _numberState = NumberState.ExponentSign;
                else if (current is >= '0' and <= '9')
                    _numberState = NumberState.Exponent;
                else
                    throw Error("An exponent digit was expected");
                return true;

            case NumberState.ExponentSign:
                if (current is not (>= '0' and <= '9'))
                    throw Error("An exponent digit was expected");
                _numberState = NumberState.Exponent;
                return true;

            case NumberState.Exponent:
                if (current is >= '0' and <= '9')
                    return true;
                return CompleteNumberAtDelimiter(current);

            default:
                throw new InvalidOperationException("Invalid JSON number parser state.");
        }
    }

    private bool CompleteNumberAtDelimiter(char current)
    {
        if (!IsValueDelimiter(current))
            throw Error("Invalid character after a JSON number");
        CompletePrimitive(isNull: false);
        return false;
    }

    private void BeginPrimitive(TokenKind token, int ordinal, SpanInfo label, string? decodedLabel)
    {
        _token = token;
        _tokenStart = _position;
        _tokenOrdinal = ordinal;
        _tokenLabel = label;
        _tokenDecodedLabel = decodedLabel;
    }

    private void CompletePrimitive(bool isNull)
    {
        SetField(CompletedSpan(_tokenStart, _position - _tokenStart, isNull: isNull), isNull);
        _token = TokenKind.None;
        CompleteParentValue();
    }

    private void SetField(SpanInfo value, bool isNull)
    {
        if (_tokenOrdinal < 0)
            return;
        _fields[_tokenOrdinal] = new FieldSpan(
            value with { IsNull = isNull },
            _tokenLabel,
            DecodedLabel: _tokenDecodedLabel);
    }

    private void Push(
        ContainerKind kind,
        int start,
        int ordinal,
        SpanInfo label,
        string? decodedLabel)
    {
        if (_depth == _stack.Length)
            Array.Resize(ref _stack, _stack.Length * 2);
        _stack[_depth++] = new ContainerFrame(
            kind,
            kind == ContainerKind.Object ? ContainerState.PropertyOrEnd : ContainerState.ValueOrEnd,
            start,
            ordinal,
            label,
            decodedLabel);
    }

    private bool CloseContainer(ContainerKind expected)
    {
        var frame = _stack[_depth - 1];
        if (frame.Kind != expected)
            throw Error($"Unexpected character '{(expected == ContainerKind.Object ? '}' : ']')}'");

        var canClose = frame.Kind == ContainerKind.Object
            ? frame.State is ContainerState.PropertyOrEnd or ContainerState.CommaOrEnd
            : frame.State is ContainerState.ValueOrEnd or ContainerState.CommaOrEnd;
        if (!canClose)
            throw Error("A JSON value or property is incomplete");

        _depth--;
        if (_depth == 0)
        {
            for (var ordinal = 0; ordinal < _fields.Length; ordinal++)
            {
                if (!_fields[ordinal].Value.IsStarted)
                    throw Error($"Projected property '{_projectedProperties[ordinal]}' was not found");
            }
            _complete = true;
            return true;
        }

        if (frame.ProjectedOrdinal >= 0)
        {
            _tokenOrdinal = frame.ProjectedOrdinal;
            _tokenLabel = frame.Label;
            _tokenDecodedLabel = frame.DecodedLabel;
            SetField(CompletedSpan(frame.Start, _position - frame.Start + 1), isNull: false);
        }
        CompleteParentValue();
        return true;
    }

    private void CompleteParentValue()
    {
        ref var parent = ref _stack[_depth - 1];
        parent.State = ContainerState.CommaOrEnd;
    }

    private InvalidDataException Error(string message)
        => new($"{message} at position {_startPosition + _position}.");

    private static bool IsWhitespace(char value)
        => value is ' ' or '\t' or '\r' or '\n';

    private static bool IsValueDelimiter(char value)
        => IsWhitespace(value) || value is ',' or ']' or '}';

    private static int HexValue(char value)
        => value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1
        };

    private static SpanInfo CompletedSpan(
        int start,
        int length,
        bool wasQuoted = false,
        bool isEscaped = false,
        bool isNull = false)
        => new(start, length, wasQuoted, isEscaped, IsStarted: true, IsComplete: true, IsNull: isNull);

    private enum TokenKind { None, String, Literal, Number }
    private enum ContainerKind { Object, Array }
    private enum ContainerState { PropertyOrEnd, Property, Colon, ValueOrEnd, Value, CommaOrEnd }
    private enum NumberState { AfterMinus, Zero, Integer, Dot, Fraction, ExponentMark, ExponentSign, Exponent }
    private enum StringEscapeState { None, AfterSlash, Unicode, LowSurrogateSlash, LowSurrogateU }

    private struct ContainerFrame(
        ContainerKind kind,
        ContainerState state,
        int start,
        int projectedOrdinal,
        SpanInfo label,
        string? decodedLabel)
    {
        public ContainerKind Kind = kind;
        public ContainerState State = state;
        public int Start = start;
        public int ProjectedOrdinal = projectedOrdinal;
        public SpanInfo Label = label;
        public string? DecodedLabel = decodedLabel;
    }
}
