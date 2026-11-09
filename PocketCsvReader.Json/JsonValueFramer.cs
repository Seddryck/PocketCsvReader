namespace PocketCsvReader.Json;

internal sealed class JsonValueFramer(bool allowComments, bool closeObject = false) : IJsonValueFramer
{
    private char[] _openings = new char[16];
    private char _first;
    private long _startPosition;
    private int _depth;
    private int _total;
    private bool _inString;
    private bool _escaping;
    private ValueKind _kind;
    private JsonCommentScanner _comments;

    public int Delimiter { get; private set; }

    public void Prepare(char first, long startPosition)
    {
        _first = first;
        _startPosition = startPosition;
    }

    public void Reset()
    {
        _comments = default;
        _depth = 0;
        _total = 0;
        _inString = _first == '"';
        _escaping = false;
        Delimiter = -1;
        _kind = _first switch
        {
            '{' or '[' => ValueKind.Composite,
            '"' => ValueKind.String,
            _ => ValueKind.Scalar
        };

        if (_kind == ValueKind.Composite)
        {
            _openings[0] = _first;
            _depth = 1;
        }
    }

    public FrameScanResult Scan(ReadOnlySpan<char> input)
    {
        for (var index = 0; index < input.Length; index++)
        {
            var current = input[index];
            if (_total++ == 0)
                continue;

            if (_kind == ValueKind.Scalar && TryCompleteScalar(current, out var scalarLength))
                return new FrameScanResult(index + 1, true, scalarLength);

            if (_kind != ValueKind.Scalar && TryCompleteStructuredValue(current))
                return new FrameScanResult(index + 1, true, _total);
        }

        return new FrameScanResult(input.Length, false);
    }

    public FrameScanResult CompleteAtEof()
    {
        if (allowComments) _comments.CompleteAtEof();
        if (_total == 0)
            return default;
        if (_kind == ValueKind.Scalar)
            return new FrameScanResult(0, true, _total);

        var description = _kind == ValueKind.String ? "string" : "value";
        throw new InvalidDataException($"The JSON {description} is incomplete at position {_startPosition + _total}.");
    }

    private bool TryCompleteScalar(char current, out int contentLength)
    {
        contentLength = _total - 1;
        if (current is not (' ' or '\t' or '\r' or '\n') && current is not (',' or ']') && !(closeObject && current == '}') && !(allowComments && current == '/'))
            return false;

        Delimiter = current;
        return true;
    }

    private bool TryCompleteStructuredValue(char current)
    {
        if (_inString)
            return ProcessStringCharacter(current);

        if (allowComments && _comments.TryConsume(current, _startPosition + _total - 1))
            return false;

        return ProcessStructuralCharacter(current);
    }

    private bool ProcessStringCharacter(char current)
    {
        if (_escaping)
        {
            _escaping = false;
            return false;
        }

        if (current == '\\')
        {
            _escaping = true;
            return false;
        }

        if (current != '"')
            return false;

        _inString = false;
        return _kind == ValueKind.String;
    }

    private bool ProcessStructuralCharacter(char current)
    {
        if (current == '"')
        {
            _inString = true;
            return false;
        }

        if (current is '{' or '[')
        {
            EnsureStackCapacity(_depth + 1);
            _openings[_depth++] = current;
            return false;
        }

        if (current is not ('}' or ']'))
            return false;

        CloseComposite(current);
        return _depth == 0;
    }

    private void CloseComposite(char current)
    {
        var expected = current == '}' ? '{' : '[';
        if (_depth == 0 || _openings[_depth - 1] != expected)
        {
            throw new InvalidDataException(
                $"Unexpected character '{current}' at position {_startPosition + _total - 1}.");
        }

        _depth--;
    }

    private void EnsureStackCapacity(int required)
    {
        if (_openings.Length >= required)
            return;
        Array.Resize(ref _openings, _openings.Length * 2);
    }

    private enum ValueKind
    {
        Scalar,
        String,
        Composite
    }
}
