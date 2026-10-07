using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

internal sealed class JsonRecordSource : IRecordSource<JsonProfile>
{
    private static readonly char[] JsonWhitespaces = [' ', '\t', '\r', '\n'];
    private readonly BufferedRecordReader _cursor;
    private readonly JsonValueFramer _valueFramer = new();
    private DocumentState _state;
    private bool _needsArrayElement;

    public JsonProfile Profile { get; }

    public JsonRecordSource(StreamReader reader, JsonProfile profile)
    {
        Profile = profile;
        _cursor = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize);
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        _cursor.BeginOperation();
        if (_state == DocumentState.Complete)
            return EndOfFile(out record, out recordState);

        var first = _state == DocumentState.BeforeRoot
            ? PeekFirstRootCharacter()
            : PeekFirstArrayElementCharacter();

        if (first < 0)
            return EndOfFile(out record, out recordState);

        var recordStart = _cursor.Position;
        var (capture, delimiter) = ReadValue((char)first);
        _cursor.Protect(capture);
        var fields = ParseFields(capture.Memory, recordStart);

        var isEndOfFile = _state == DocumentState.SingleRoot
            ? CompleteSingleRoot(delimiter)
            : CompleteArrayElement(delimiter);

        record = RecordSpan.FromMemory(capture.Memory, fields);
        recordState = RecordState.Record;
        return isEndOfFile;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cursor.BeginOperation();
        if (_state == DocumentState.Complete)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var first = _state == DocumentState.BeforeRoot
            ? await PeekFirstRootCharacterAsync(cancellationToken).ConfigureAwait(false)
            : await PeekFirstArrayElementCharacterAsync(cancellationToken).ConfigureAwait(false);

        if (first < 0)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var recordStart = _cursor.Position;
        var (capture, delimiter) = await ReadValueAsync((char)first, cancellationToken).ConfigureAwait(false);
        _cursor.Protect(capture);
        var fields = ParseFields(capture.Memory, recordStart);
        var isEndOfFile = _state == DocumentState.SingleRoot
            ? await CompleteSingleRootAsync(delimiter, cancellationToken).ConfigureAwait(false)
            : await CompleteArrayElementAsync(delimiter, cancellationToken).ConfigureAwait(false);

        return new(isEndOfFile, new RecordMemory(capture.Memory, fields), RecordState.Record);
    }

    private static FieldSpan[] ParseFields(ReadOnlyMemory<char> json, long recordStart)
    {
        try
        {
            return JsonRecordParser.Parse(json.Span, JsonWhitespaces);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Invalid JSON value starting at position {recordStart}: {exception.Message}", exception);
        }
    }

    private int PeekFirstRootCharacter()
    {
        var first = PeekNonWhitespace();
        if (first < 0)
            throw Error("A JSON value was expected");

        if (first != '[')
        {
            _state = DocumentState.SingleRoot;
            return first;
        }

        _cursor.Read();
        _state = DocumentState.Array;
        var arrayFirst = PeekNonWhitespace();
        if (arrayFirst < 0)
            throw Error("The top-level array is incomplete");
        if (arrayFirst != ']')
            return arrayFirst;

        _cursor.Read();
        CompleteDocument();
        return -1;
    }

    private int PeekFirstArrayElementCharacter()
    {
        if (!_needsArrayElement)
            throw new InvalidOperationException("The JSON document reader is not positioned at an array element.");

        _needsArrayElement = false;
        var first = PeekNonWhitespace();
        if (first < 0)
            throw Error("The top-level array is incomplete");
        if (first == ']')
            throw Error("A JSON array cannot end with a trailing comma");
        return first;
    }

    private async ValueTask<int> PeekFirstRootCharacterAsync(CancellationToken cancellationToken)
    {
        var first = await PeekNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (first < 0)
            throw Error("A JSON value was expected");

        if (first != '[')
        {
            _state = DocumentState.SingleRoot;
            return first;
        }

        await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
        _state = DocumentState.Array;
        var arrayFirst = await PeekNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (arrayFirst < 0)
            throw Error("The top-level array is incomplete");
        if (arrayFirst != ']')
            return arrayFirst;

        await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
        await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
        return -1;
    }

    private async ValueTask<int> PeekFirstArrayElementCharacterAsync(CancellationToken cancellationToken)
    {
        if (!_needsArrayElement)
            throw new InvalidOperationException("The JSON document reader is not positioned at an array element.");

        _needsArrayElement = false;
        var first = await PeekNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (first < 0)
            throw Error("The top-level array is incomplete");
        if (first == ']')
            throw Error("A JSON array cannot end with a trailing comma");
        return first;
    }

    private (CapturedRecord Json, int Delimiter) ReadValue(char first)
    {
        _valueFramer.Prepare(first, _cursor.Position);
        var capture = _cursor.ReadFrame(_valueFramer)
            ?? throw Error("The JSON value is incomplete");
        return (capture, _valueFramer.Delimiter);
    }

    private async ValueTask<(CapturedRecord Json, int Delimiter)> ReadValueAsync(
        char first,
        CancellationToken cancellationToken)
    {
        _valueFramer.Prepare(first, _cursor.Position);
        var capture = await _cursor.ReadFrameAsync(_valueFramer, cancellationToken).ConfigureAwait(false)
            ?? throw Error("The JSON value is incomplete");
        return (capture, _valueFramer.Delimiter);
    }

    private bool CompleteSingleRoot(int delimiter)
    {
        if (delimiter >= 0 && !IsJsonWhitespace((char)delimiter))
            throw Error($"Unexpected character '{(char)delimiter}' after the root value");
        CompleteDocument();
        return true;
    }

    private bool CompleteArrayElement(int delimiter)
    {
        var next = delimiter >= 0 && !IsJsonWhitespace((char)delimiter)
            ? delimiter
            : ReadNonWhitespace();

        if (next == ',')
        {
            _needsArrayElement = true;
            return false;
        }
        if (next == ']')
        {
            CompleteDocument();
            return true;
        }
        if (next < 0)
            throw Error("The top-level array is incomplete");
        throw Error($"Expected ',' or ']' but found '{(char)next}'");
    }

    private async ValueTask<bool> CompleteSingleRootAsync(int delimiter, CancellationToken cancellationToken)
    {
        if (delimiter >= 0 && !IsJsonWhitespace((char)delimiter))
            throw Error($"Unexpected character '{(char)delimiter}' after the root value");
        await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> CompleteArrayElementAsync(int delimiter, CancellationToken cancellationToken)
    {
        var next = delimiter >= 0 && !IsJsonWhitespace((char)delimiter)
            ? delimiter
            : await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);

        if (next == ',')
        {
            _needsArrayElement = true;
            return false;
        }
        if (next == ']')
        {
            await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        if (next < 0)
            throw Error("The top-level array is incomplete");
        throw Error($"Expected ',' or ']' but found '{(char)next}'");
    }

    private void CompleteDocument()
    {
        var next = ReadNonWhitespace();
        if (next >= 0)
            throw Error($"Unexpected character '{(char)next}' after the root value");
        _state = DocumentState.Complete;
    }

    private async ValueTask CompleteDocumentAsync(CancellationToken cancellationToken)
    {
        var next = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (next >= 0)
            throw Error($"Unexpected character '{(char)next}' after the root value");
        _state = DocumentState.Complete;
    }

    private int PeekNonWhitespace()
    {
        int current;
        while ((current = _cursor.Peek()) >= 0 && IsJsonWhitespace((char)current))
            _cursor.Read();
        return current;
    }

    private int ReadNonWhitespace()
    {
        var current = PeekNonWhitespace();
        return current < 0 ? current : _cursor.Read();
    }

    private async ValueTask<int> PeekNonWhitespaceAsync(CancellationToken cancellationToken)
    {
        int current;
        while ((current = await _cursor.PeekAsync(cancellationToken).ConfigureAwait(false)) >= 0
               && IsJsonWhitespace((char)current))
        {
            await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        return current;
    }

    private async ValueTask<int> ReadNonWhitespaceAsync(CancellationToken cancellationToken)
    {
        var current = await PeekNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        return current < 0
            ? current
            : await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private InvalidDataException Error(string message)
        => new($"{message} at position {_cursor.Position}.");

    private static bool IsJsonWhitespace(char value)
        => value is ' ' or '\t' or '\r' or '\n';

    private static bool EndOfFile(out RecordSpan record, out RecordState recordState)
    {
        record = new RecordSpan([], []);
        recordState = RecordState.Eof;
        return true;
    }

    public void Dispose()
        => _cursor.Dispose();

    private enum DocumentState
    {
        BeforeRoot,
        SingleRoot,
        Array,
        Complete
    }

    private sealed class JsonValueFramer : IRecordFramer
    {
        private char[] _openings = new char[16];
        private char _first;
        private long _startPosition;
        private int _depth;
        private int _total;
        private bool _inString;
        private bool _escaping;
        private ValueKind _kind;

        public int Delimiter { get; private set; }

        public void Prepare(char first, long startPosition)
        {
            _first = first;
            _startPosition = startPosition;
        }

        public void Reset()
        {
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
            if (!IsJsonWhitespace(current) && current is not (',' or ']'))
                return false;

            Delimiter = current;
            return true;
        }

        private bool TryCompleteStructuredValue(char current)
        {
            if (_inString)
                return ProcessStringCharacter(current);

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
}
