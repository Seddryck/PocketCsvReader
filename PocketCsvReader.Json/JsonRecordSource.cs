using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

internal sealed class JsonRecordSource : IRecordSource<JsonProfile>
{
    private static readonly char[] JsonWhitespaces = [' ', '\t', '\r', '\n'];
    private readonly BufferedJsonCursor _cursor;
    private DocumentState _state;
    private bool _needsArrayElement;

    public JsonProfile Profile { get; }

    public JsonRecordSource(StreamReader reader, JsonProfile profile)
    {
        Profile = profile;
        _cursor = new BufferedJsonCursor(reader, profile.ParserOptimizations.BufferSize);
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
            return new JsonRecordParser(json, JsonWhitespaces).ParseRoot();
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

    private (CapturedJson Json, int Delimiter) ReadValue(char first)
    {
        _cursor.BeginCapture();
        _cursor.Read();

        if (first is '{' or '[')
        {
            ReadComposite(first);
            return (_cursor.EndCapture(), -1);
        }

        if (first == '"')
        {
            ReadString();
            return (_cursor.EndCapture(), -1);
        }

        while (true)
        {
            var next = _cursor.Read();
            if (next < 0)
                return (_cursor.EndCapture(), -1);
            if (IsJsonWhitespace((char)next) || next is ',' or ']')
                return (_cursor.EndCapture(trimEnd: 1), next);
        }
    }

    private async ValueTask<(CapturedJson Json, int Delimiter)> ReadValueAsync(
        char first,
        CancellationToken cancellationToken)
    {
        _cursor.BeginCapture();
        await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (first is '{' or '[')
        {
            await ReadCompositeAsync(first, cancellationToken).ConfigureAwait(false);
            return (_cursor.EndCapture(), -1);
        }

        if (first == '"')
        {
            await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            return (_cursor.EndCapture(), -1);
        }

        while (true)
        {
            var next = await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                return (_cursor.EndCapture(), -1);
            if (IsJsonWhitespace((char)next) || next is ',' or ']')
                return (_cursor.EndCapture(trimEnd: 1), next);
        }
    }

    private void ReadComposite(char opening)
    {
        var scanner = new CompositeScanner(opening);
        while (!scanner.IsComplete)
        {
            var next = _cursor.Read();
            if (next < 0)
                throw Error("The JSON value is incomplete");
            if (!scanner.Accept((char)next))
                throw Error($"Unexpected character '{(char)next}'");
        }
    }

    private async ValueTask ReadCompositeAsync(char opening, CancellationToken cancellationToken)
    {
        var scanner = new CompositeScanner(opening);
        while (!scanner.IsComplete)
        {
            var next = await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                throw Error("The JSON value is incomplete");
            if (!scanner.Accept((char)next))
                throw Error($"Unexpected character '{(char)next}'");
        }
    }

    private void ReadString()
    {
        var escaping = false;
        while (true)
        {
            var next = _cursor.Read();
            if (next < 0)
                throw Error("The JSON string is incomplete");
            if (escaping)
                escaping = false;
            else if (next == '\\')
                escaping = true;
            else if (next == '"')
                return;
        }
    }

    private async ValueTask ReadStringAsync(CancellationToken cancellationToken)
    {
        var escaping = false;
        while (true)
        {
            var next = await _cursor.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                throw Error("The JSON string is incomplete");
            if (escaping)
                escaping = false;
            else if (next == '\\')
                escaping = true;
            else if (next == '"')
                return;
        }
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

    private sealed class CompositeScanner(char opening)
    {
        private readonly Stack<char> _openings = new([opening]);
        private bool _inString;
        private bool _escaping;

        public bool IsComplete => _openings.Count == 0;

        public bool Accept(char current)
        {
            if (_inString)
            {
                AcceptStringCharacter(current);
                return true;
            }

            return AcceptStructuralCharacter(current);
        }

        private void AcceptStringCharacter(char current)
        {
            if (_escaping)
                _escaping = false;
            else if (current == '\\')
                _escaping = true;
            else if (current == '"')
                _inString = false;
        }

        private bool AcceptStructuralCharacter(char current)
        {
            switch (current)
            {
                case '"':
                    _inString = true;
                    return true;
                case '{':
                case '[':
                    _openings.Push(current);
                    return true;
                case '}':
                    return _openings.Pop() == '{';
                case ']':
                    return _openings.Pop() == '[';
                default:
                    return true;
            }
        }
    }
}
