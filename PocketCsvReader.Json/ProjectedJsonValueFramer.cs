using System.Buffers;

namespace PocketCsvReader.Json;

internal sealed class ProjectedJsonValueFramer : IJsonValueFramer
{
    private const ulong PropertyHashOffset = 14695981039346656037UL;
    private const ulong PropertyHashPrime = 1099511628211UL;
    private static readonly SearchValues<char> StringSpecialCharacters = SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F\"\\");

    private readonly IReadOnlyList<string> _projectedProperties;
    private readonly ulong[]? _projectedPropertyHashes;
    private readonly int[]? _projectedPropertyLookup;
    private readonly int _projectedPropertyLookupMask;
    private readonly char[]? _propertyNameBuffer;
    private readonly FieldSpan[] _fields;
    private readonly bool _orderedProjection;
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
    private ulong _propertyHash;
    private bool _propertyMatchesExpected;
    private int _nextProjectedOrdinal;
    private int _remainingProjectedFields;
    private bool _complete;

    public int Delimiter { get; private set; }
    public bool RequiresValueMaterialization { get; private set; }

    public ProjectedJsonValueFramer(
        IReadOnlyList<string> projectedProperties,
        FieldSpan[] fields,
        bool orderedProjection = false)
    {
        _projectedProperties = projectedProperties;
        _fields = fields;
        _orderedProjection = orderedProjection;
        if (orderedProjection)
            return;

        _projectedPropertyHashes = new ulong[projectedProperties.Count];
        var lookupCapacity = 1;
        while (lookupCapacity < projectedProperties.Count * 2)
            lookupCapacity <<= 1;
        _projectedPropertyLookup = new int[lookupCapacity];
        _projectedPropertyLookupMask = lookupCapacity - 1;

        var maximumPropertyLength = 0;
        for (var ordinal = 0; ordinal < projectedProperties.Count; ordinal++)
        {
            var property = projectedProperties[ordinal];
            var hash = HashProperty(property);
            _projectedPropertyHashes[ordinal] = hash;
            var slot = GetPropertyLookupSlot(hash);
            while (_projectedPropertyLookup[slot] != 0)
                slot = (slot + 1) & _projectedPropertyLookupMask;
            _projectedPropertyLookup[slot] = ordinal + 1;
            maximumPropertyLength = Math.Max(maximumPropertyLength, property.Length);
        }
        _propertyNameBuffer = new char[maximumPropertyLength];
    }

    public void Prepare(char first, long startPosition)
        => _startPosition = startPosition;

    public void Reset()
    {
        Array.Clear(_fields);
        _depth = 0;
        _position = 0;
        _token = TokenKind.None;
        _nextProjectedOrdinal = 0;
        _remainingProjectedFields = _fields.Length;
        _complete = false;
        Delimiter = -1;
        RequiresValueMaterialization = false;
    }

    public FrameScanResult Scan(ReadOnlySpan<char> input)
    {
        var index = 0;
        while (index < input.Length)
        {
            if (_token == TokenKind.String)
            {
                ScanString(input, ref index);
                if (_complete)
                    return new FrameScanResult(index, true, _position);
                continue;
            }

            if (_token == TokenKind.Literal)
            {
                ScanLiteral(input, ref index);
                continue;
            }

            if (_token == TokenKind.Number)
            {
                ScanNumber(input, ref index);
                continue;
            }

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

        if (isProperty && _depth == 1 && _remainingProjectedFields > 0)
        {
            _propertyLength = 0;
            if (_orderedProjection)
                _propertyMatchesExpected = true;
            else
                _propertyHash = PropertyHashOffset;
        }
    }

    private void ScanString(ReadOnlySpan<char> input, ref int index)
    {
        while (index < input.Length && _token == TokenKind.String)
        {
            if (_stringEscapeState != StringEscapeState.None)
            {
                ProcessStringEscape(input[index]);
                index++;
                _position++;
                continue;
            }

            var remaining = input[index..];
            var specialOffset = remaining.IndexOfAny(StringSpecialCharacters);
            if (specialOffset < 0)
            {
                AppendPropertyCharacters(remaining);
                _position += remaining.Length;
                index = input.Length;
                return;
            }

            if (specialOffset > 0)
            {
                AppendPropertyCharacters(remaining[..specialOffset]);
                index += specialOffset;
                _position += specialOffset;
            }

            var current = input[index];
            if (current < ' ')
                throw Error("An unescaped control character is not allowed in a JSON string");

            if (current == '\\')
            {
                _stringWasEscaped = true;
                if (!_stringIsProperty && _tokenOrdinal >= 0)
                    RequiresValueMaterialization = true;
                _stringEscapeState = StringEscapeState.AfterSlash;
            }
            else
            {
                if (_stringIsProperty)
                    CompletePropertyName();
                else
                    CompleteStringValue();
                _token = TokenKind.None;
            }

            index++;
            _position++;
        }
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
                AppendPropertyCharacter(decoded);
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
            AppendPropertyCharacter(_highSurrogate);
            AppendPropertyCharacter(codeUnit);
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

        AppendPropertyCharacter(codeUnit);
        _stringEscapeState = StringEscapeState.None;
    }

    private void AppendPropertyCharacters(ReadOnlySpan<char> value)
    {
        if (!_stringIsProperty || _depth != 1 || _remainingProjectedFields == 0)
            return;

        if (_orderedProjection)
        {
            if (_propertyMatchesExpected)
            {
                var expected = _projectedProperties[_nextProjectedOrdinal];
                _propertyMatchesExpected = _propertyLength + value.Length <= expected.Length
                    && value.SequenceEqual(expected.AsSpan(_propertyLength, value.Length));
            }
            _propertyLength += value.Length;
            return;
        }

        foreach (var character in value)
            AppendPropertyCharacter(character);
    }

    private void AppendPropertyCharacter(char value)
    {
        if (!_stringIsProperty || _depth != 1 || _remainingProjectedFields == 0)
            return;

        if (_orderedProjection)
        {
            var expected = _projectedProperties[_nextProjectedOrdinal];
            if (_propertyLength >= expected.Length || expected[_propertyLength] != value)
                _propertyMatchesExpected = false;
            _propertyLength++;
            return;
        }

        if (_propertyLength < _propertyNameBuffer!.Length)
            _propertyNameBuffer[_propertyLength] = value;
        _propertyLength++;
        _propertyHash = (_propertyHash ^ value) * PropertyHashPrime;
    }

    private void CompletePropertyName()
    {
        ref var frame = ref _stack[_depth - 1];
        var ordinal = _depth == 1 && _remainingProjectedFields > 0
            ? GetProjectedOrdinal()
            : -1;

        frame.ProjectedOrdinal = ordinal;
        frame.Label = ordinal >= 0
            ? CompletedSpan(
                _tokenStart,
                _position - _tokenStart,
                wasQuoted: true,
                isEscaped: _stringWasEscaped)
            : default;
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

    private int GetProjectedOrdinal()
    {
        if (_orderedProjection)
        {
            var expected = _projectedProperties[_nextProjectedOrdinal];
            return _propertyMatchesExpected && _propertyLength == expected.Length
                ? _nextProjectedOrdinal
                : -1;
        }

        var slot = GetPropertyLookupSlot(_propertyHash);
        while (_projectedPropertyLookup![slot] != 0)
        {
            var candidate = _projectedPropertyLookup[slot] - 1;
            var projectedProperty = _projectedProperties[candidate];
            if (_projectedPropertyHashes![candidate] == _propertyHash
                && projectedProperty.Length == _propertyLength
                && _propertyNameBuffer!.AsSpan(0, _propertyLength).SequenceEqual(projectedProperty))
            {
                return candidate;
            }
            slot = (slot + 1) & _projectedPropertyLookupMask;
        }
        return -1;
    }

    private int GetPropertyLookupSlot(ulong hash)
        => (int)(hash & (ulong)_projectedPropertyLookupMask);

    private void ScanLiteral(ReadOnlySpan<char> input, ref int index)
    {
        var remainingLiteral = _literal.AsSpan(_literalIndex);
        var available = Math.Min(remainingLiteral.Length, input.Length - index);
        var inputPart = input.Slice(index, available);
        if (!inputPart.SequenceEqual(remainingLiteral[..available]))
        {
            for (var offset = 0; offset < available; offset++)
            {
                if (inputPart[offset] != remainingLiteral[offset])
                {
                    _position += offset;
                    index += offset;
                    throw Error("Invalid JSON literal");
                }
            }
        }

        _literalIndex += available;
        _position += available;
        index += available;
        if (_literalIndex < _literal.Length || index == input.Length)
            return;

        if (!IsValueDelimiter(input[index]))
            throw Error("Invalid character after a JSON literal");
        CompletePrimitive(isNull: _literal == "null");
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

    private void ScanNumber(ReadOnlySpan<char> input, ref int index)
    {
        while (index < input.Length && _token == TokenKind.Number)
        {
            var current = input[index];
            switch (_numberState)
            {
                case NumberState.AfterMinus:
                    if (current == '0')
                        _numberState = NumberState.Zero;
                    else if (current is >= '1' and <= '9')
                        _numberState = NumberState.Integer;
                    else
                        throw Error("A digit was expected after the JSON number sign");
                    Consume(ref index);
                    break;

                case NumberState.Zero:
                    if (current == '.')
                    {
                        _numberState = NumberState.Dot;
                        Consume(ref index);
                    }
                    else if (current is 'e' or 'E')
                    {
                        _numberState = NumberState.ExponentMark;
                        Consume(ref index);
                    }
                    else if (current is >= '0' and <= '9')
                        throw Error("A JSON number cannot contain a leading zero");
                    else
                        CompleteNumberAtDelimiter(current);
                    break;

                case NumberState.Integer:
                    ConsumeDigits(input, ref index);
                    if (index == input.Length)
                        break;
                    current = input[index];
                    if (current == '.')
                    {
                        _numberState = NumberState.Dot;
                        Consume(ref index);
                    }
                    else if (current is 'e' or 'E')
                    {
                        _numberState = NumberState.ExponentMark;
                        Consume(ref index);
                    }
                    else
                        CompleteNumberAtDelimiter(current);
                    break;

                case NumberState.Dot:
                    if (current is not (>= '0' and <= '9'))
                        throw Error("A fractional digit was expected");
                    _numberState = NumberState.Fraction;
                    Consume(ref index);
                    break;

                case NumberState.Fraction:
                    ConsumeDigits(input, ref index);
                    if (index == input.Length)
                        break;
                    current = input[index];
                    if (current is 'e' or 'E')
                    {
                        _numberState = NumberState.ExponentMark;
                        Consume(ref index);
                    }
                    else
                        CompleteNumberAtDelimiter(current);
                    break;

                case NumberState.ExponentMark:
                    if (current is '+' or '-')
                        _numberState = NumberState.ExponentSign;
                    else if (current is >= '0' and <= '9')
                        _numberState = NumberState.Exponent;
                    else
                        throw Error("An exponent digit was expected");
                    Consume(ref index);
                    break;

                case NumberState.ExponentSign:
                    if (current is not (>= '0' and <= '9'))
                        throw Error("An exponent digit was expected");
                    _numberState = NumberState.Exponent;
                    Consume(ref index);
                    break;

                case NumberState.Exponent:
                    ConsumeDigits(input, ref index);
                    if (index < input.Length)
                        CompleteNumberAtDelimiter(input[index]);
                    break;

                default:
                    throw new InvalidOperationException("Invalid JSON number parser state.");
            }
        }
    }

    private void ConsumeDigits(ReadOnlySpan<char> input, ref int index)
    {
        var start = index;
        while (index < input.Length && input[index] is >= '0' and <= '9')
            index++;
        _position += index - start;
    }

    private void Consume(ref int index)
    {
        index++;
        _position++;
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
        var wasUnset = !_fields[_tokenOrdinal].Value.IsStarted;
        _fields[_tokenOrdinal] = new FieldSpan(
            value with { IsNull = isNull },
            _tokenLabel,
            DecodedLabel: _tokenDecodedLabel);
        if (wasUnset)
        {
            _remainingProjectedFields--;
            if (_orderedProjection)
                _nextProjectedOrdinal++;
        }
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

    private static ulong HashProperty(ReadOnlySpan<char> property)
    {
        var hash = PropertyHashOffset;
        foreach (var character in property)
            hash = (hash ^ character) * PropertyHashPrime;
        return hash;
    }

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
