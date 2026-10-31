using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

internal sealed class JsonRecordSource : IRecordSource<JsonProfile>, IPositionSource
{
    private static readonly char[] JsonWhitespaces = [' ', '\t', '\r', '\n'];
    private readonly BufferedRecordReader _cursor;
    private readonly PositionTracker? _position;
    private ReadOnlyMemory<char> _selectionInput;
    private RecordPosition _selectionStart;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    private readonly IJsonValueFramer _valueFramer;
    private readonly ProjectedJsonValueFramer? _projectedValueFramer;
    // The current record owns this buffer until the reader advances to the next record.
    private readonly FieldSpan[]? _projectedFields;
    private IEnumerator<ReadOnlyMemory<char>>? _matches;
    private DocumentState _state;
    private bool _needsArrayElement;
    private JsonCommentScanner _trivia;

    public JsonProfile Profile { get; }

    public JsonRecordSource(StreamReader reader, JsonProfile profile)
    {
        Profile = profile;
        if (profile.TrackPosition) _position = new();
        _cursor = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
        _projectedFields = profile.ProjectedProperties is null
            ? null
            : new FieldSpan[profile.ProjectedProperties.Count];
        _projectedValueFramer = profile.ProjectedProperties is null
            ? null
            : new ProjectedJsonValueFramer(
                profile.ProjectedProperties,
                _projectedFields!,
                profile.OrderedProjection,
                profile.Jsonc);
        _valueFramer = _projectedValueFramer is null
            ? new JsonValueFramer(Profile.Jsonc)
            : _projectedValueFramer;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (Profile.ParsedRecordPath is not null)
            return ReadSelected(out record, out recordState);
        _cursor.BeginOperation();
        if (_state == DocumentState.Complete)
            return EndOfFile(out record, out recordState);

        var first = _state == DocumentState.BeforeRoot
            ? PeekFirstRootCharacter()
            : PeekFirstArrayElementCharacter();

        if (first < 0)
            return EndOfFile(out record, out recordState);

        _position?.BeginRecord();
        var recordStart = _cursor.Position;
        var (capture, delimiter) = ReadValue((char)first);
        _cursor.Protect(capture);
        var fields = GetFields(capture.Memory, recordStart);

        var isEndOfFile = _state == DocumentState.SingleRoot
            ? CompleteSingleRoot(delimiter)
            : CompleteArrayElement(delimiter);

        _position?.CompleteRecord();
        record = RecordSpan.FromMemory(capture.Memory, fields);
        recordState = RecordState.Record;
        return isEndOfFile;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Profile.ParsedRecordPath is not null)
            return await ReadSelectedAsync(cancellationToken).ConfigureAwait(false);
        _cursor.BeginOperation();
        if (_state == DocumentState.Complete)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var first = _state == DocumentState.BeforeRoot
            ? await PeekFirstRootCharacterAsync(cancellationToken).ConfigureAwait(false)
            : await PeekFirstArrayElementCharacterAsync(cancellationToken).ConfigureAwait(false);

        if (first < 0)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        _position?.BeginRecord();
        var recordStart = _cursor.Position;
        var (capture, delimiter) = await ReadValueAsync((char)first, cancellationToken).ConfigureAwait(false);
        _cursor.Protect(capture);
        var fields = GetFields(capture.Memory, recordStart);
        var isEndOfFile = _state == DocumentState.SingleRoot
            ? await CompleteSingleRootAsync(delimiter, cancellationToken).ConfigureAwait(false)
            : await CompleteArrayElementAsync(delimiter, cancellationToken).ConfigureAwait(false);

        _position?.CompleteRecord();
        return new(isEndOfFile, new RecordMemory(capture.Memory, fields), RecordState.Record);
    }

    private bool ReadSelected(out RecordSpan record, out RecordState recordState)
    {
        if (_matches is null && _state != DocumentState.Complete)
        {
            _cursor.BeginOperation();
            var first = PeekNonWhitespace();
            if (first < 0)
                throw Error("A JSON value was expected");
            _position?.BeginRecord();
            var recordStart = _cursor.Position;
            var framer = new JsonValueFramer(Profile.Jsonc);
            framer.Prepare((char)first, _cursor.Position);
            var capture = _cursor.ReadFrame(framer) ?? throw Error("The JSON value is incomplete");
            _cursor.Protect(capture);
            CompleteSingleRoot(framer.Delimiter);
            _selectionInput = capture.Memory;
            _selectionStart = _position?.PendingPosition ?? default;
            _matches = Profile.ParsedRecordPath!.Select(capture.Memory, JsonWhitespaces, ParseFields(capture.Memory, recordStart), Profile.Jsonc).GetEnumerator();
        }
        if (TrySelectRow(out var row))
        {
            record = RecordSpan.FromMemory(row.Span, row.FieldSpans);
            recordState = RecordState.Record;
            return false;
        }
        return EndOfFile(out record, out recordState);
    }

    private async ValueTask<RecordReadResult> ReadSelectedAsync(CancellationToken cancellationToken)
    {
        if (_matches is null && _state != DocumentState.Complete)
        {
            _cursor.BeginOperation();
            var first = await PeekNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
            if (first < 0)
                throw Error("A JSON value was expected");
            _position?.BeginRecord();
            var recordStart = _cursor.Position;
            var framer = new JsonValueFramer(Profile.Jsonc);
            framer.Prepare((char)first, _cursor.Position);
            var capture = await _cursor.ReadFrameAsync(framer, cancellationToken).ConfigureAwait(false)
                ?? throw Error("The JSON value is incomplete");
            _cursor.Protect(capture);
            await CompleteSingleRootAsync(framer.Delimiter, cancellationToken).ConfigureAwait(false);
            _selectionInput = capture.Memory;
            _selectionStart = _position?.PendingPosition ?? default;
            _matches = Profile.ParsedRecordPath!.Select(capture.Memory, JsonWhitespaces, ParseFields(capture.Memory, recordStart), Profile.Jsonc).GetEnumerator();
        }
        return TrySelectRow(out var row)
            ? new(false, row, RecordState.Record)
            : new(true, RecordMemory.Empty, RecordState.Eof);
    }

    private bool TrySelectRow(out RecordMemory row)
    {
        if (_matches?.MoveNext() == true)
        {
            var json = _matches.Current;
            if (_position is not null)
            {
                if (!_selectionInput.Span.Overlaps(json.Span, out int offset))
                    throw new InvalidOperationException("Selected JSON must retain its source memory.");
                _position.CompleteRecordAt(_selectionStart, _selectionInput.Span[..offset]);
            }
            if (_projectedValueFramer is not null)
            {
                _projectedValueFramer.Prepare(json.Span[0], 0);
                _projectedValueFramer.Reset();
                var result = _projectedValueFramer.Scan(json.Span);
                if (!result.Complete)
                    _projectedValueFramer.CompleteAtEof();
            }
            row = new RecordMemory(json, GetFields(json, 0));
            return true;
        }
        _matches?.Dispose();
        _matches = null;
        row = RecordMemory.Empty;
        return false;
    }

    private FieldSpan[] ParseFields(ReadOnlyMemory<char> json, long recordStart)
    {
        try
        {
            return JsonRecordParser.Parse(json.Span, JsonWhitespaces, Profile.Jsonc);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Invalid JSON value starting at position {recordStart}: {exception.Message}", exception);
        }
    }

    private FieldSpan[] GetFields(ReadOnlyMemory<char> json, long recordStart)
    {
        if (_projectedValueFramer is null)
            return ParseFields(json, recordStart);

        if (_projectedValueFramer.RequiresValueMaterialization)
        {
            try
            {
                JsonRecordParser.MaterializeProjectedValues(json.Span, JsonWhitespaces, _projectedFields!, Profile.Jsonc);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Invalid JSON value starting at position {recordStart}: {exception.Message}", exception);
            }
        }
        return _projectedFields!;
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
        {
            if (!Profile.Jsonc)
                throw Error("A JSON array cannot end with a trailing comma");
            _cursor.Read();
            CompleteDocument();
            return -1;
        }
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
        {
            if (!Profile.Jsonc)
                throw Error("A JSON array cannot end with a trailing comma");
            await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
            await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
            return -1;
        }
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
        delimiter = BeginDelimiterTrivia(delimiter);
        if (delimiter >= 0 && !IsJsonWhitespace((char)delimiter))
            throw Error($"Unexpected character '{(char)delimiter}' after the root value");
        CompleteDocument();
        return true;
    }

    private bool CompleteArrayElement(int delimiter)
    {
        delimiter = BeginDelimiterTrivia(delimiter);
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
        delimiter = BeginDelimiterTrivia(delimiter);
        if (delimiter >= 0 && !IsJsonWhitespace((char)delimiter))
            throw Error($"Unexpected character '{(char)delimiter}' after the root value");
        await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> CompleteArrayElementAsync(int delimiter, CancellationToken cancellationToken)
    {
        delimiter = BeginDelimiterTrivia(delimiter);
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

    // Scalar framing consumes its first delimiter. Retain a consumed slash so
    // the same trivia scanner can finish the comment across refills.
    private int BeginDelimiterTrivia(int delimiter)
    {
        if (Profile.Jsonc && delimiter == '/')
        {
            _trivia.TryConsume('/', _cursor.Position - 1);
            return -1;
        }
        return delimiter;
    }

    private bool ConsumeTrivia(int current)
    {
        if (current < 0)
        {
            if (Profile.Jsonc) _trivia.CompleteAtEof();
            return false;
        }
        return (Profile.Jsonc && _trivia.TryConsume((char)current, _cursor.Position))
            || IsJsonWhitespace((char)current);
    }

    private int PeekNonWhitespace()
    {
        int current;
        while (ConsumeTrivia(current = _cursor.Peek()))
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
        while (ConsumeTrivia(current = await _cursor.PeekAsync(cancellationToken).ConfigureAwait(false)))
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
    {
        _matches?.Dispose();
        _cursor.Dispose();
    }

    private enum DocumentState
    {
        BeforeRoot,
        SingleRoot,
        Array,
        Complete
    }

    private sealed class JsonValueFramer(bool jsonc) : IJsonValueFramer
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
            if (jsonc) _comments.CompleteAtEof();
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
            if (!IsJsonWhitespace(current) && current is not (',' or ']') && !(jsonc && current == '/'))
                return false;

            Delimiter = current;
            return true;
        }

        private bool TryCompleteStructuredValue(char current)
        {
            if (_inString)
                return ProcessStringCharacter(current);

            if (jsonc && _comments.TryConsume(current, _startPosition + _total - 1))
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
}
