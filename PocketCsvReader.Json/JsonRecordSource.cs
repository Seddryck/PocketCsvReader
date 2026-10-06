using System.Text;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

internal sealed class JsonRecordSource : IRecordSource<JsonProfile>
{
    private static readonly char[] JsonWhitespaces = [' ', '\t', '\r', '\n'];
    private readonly StreamReader _reader;
    private readonly char[] _asyncBuffer = new char[1];
    private DocumentState _state;
    private bool _needsArrayElement;
    private long _position;

    public JsonProfile Profile { get; }

    public JsonRecordSource(StreamReader reader, JsonProfile profile)
    {
        _reader = reader;
        Profile = profile;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (_state == DocumentState.Complete)
            return EndOfFile(out record, out recordState);

        var first = _state == DocumentState.BeforeRoot
            ? ReadFirstRootCharacter()
            : ReadFirstArrayElementCharacter();

        if (first < 0)
            return EndOfFile(out record, out recordState);

        var recordStart = _position - 1;
        var json = ReadValue((char)first, out var delimiter);
        var fields = ParseFields(json, recordStart);

        var isEndOfFile = _state == DocumentState.SingleRoot
            ? CompleteSingleRoot(delimiter)
            : CompleteArrayElement(delimiter);

        record = new RecordSpan(json.AsSpan(), fields);
        recordState = RecordState.Record;
        return isEndOfFile;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state == DocumentState.Complete)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var first = _state == DocumentState.BeforeRoot
            ? await ReadFirstRootCharacterAsync(cancellationToken).ConfigureAwait(false)
            : await ReadFirstArrayElementCharacterAsync(cancellationToken).ConfigureAwait(false);

        if (first < 0)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var recordStart = _position - 1;
        var (json, delimiter) = await ReadValueAsync((char)first, cancellationToken).ConfigureAwait(false);
        var fields = ParseFields(json, recordStart);
        var isEndOfFile = _state == DocumentState.SingleRoot
            ? await CompleteSingleRootAsync(delimiter, cancellationToken).ConfigureAwait(false)
            : await CompleteArrayElementAsync(delimiter, cancellationToken).ConfigureAwait(false);

        return new(isEndOfFile, new RecordMemory(json.AsSpan(), fields), RecordState.Record);
    }

    private static FieldSpan[] ParseFields(string json, long recordStart)
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

    private int ReadFirstRootCharacter()
    {
        var first = ReadNonWhitespace();
        if (first < 0)
            throw Error("A JSON value was expected");

        if (first != '[')
        {
            _state = DocumentState.SingleRoot;
            return first;
        }

        _state = DocumentState.Array;
        var arrayFirst = ReadNonWhitespace();
        if (arrayFirst < 0)
            throw Error("The top-level array is incomplete");
        if (arrayFirst != ']')
            return arrayFirst;

        CompleteDocument();
        return -1;
    }

    private int ReadFirstArrayElementCharacter()
    {
        if (!_needsArrayElement)
            throw new InvalidOperationException("The JSON document reader is not positioned at an array element.");

        _needsArrayElement = false;
        var first = ReadNonWhitespace();
        if (first < 0)
            throw Error("The top-level array is incomplete");
        if (first == ']')
            throw Error("A JSON array cannot end with a trailing comma");
        return first;
    }

    private async ValueTask<int> ReadFirstRootCharacterAsync(CancellationToken cancellationToken)
    {
        var first = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (first < 0)
            throw Error("A JSON value was expected");

        if (first != '[')
        {
            _state = DocumentState.SingleRoot;
            return first;
        }

        _state = DocumentState.Array;
        var arrayFirst = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (arrayFirst < 0)
            throw Error("The top-level array is incomplete");
        if (arrayFirst != ']')
            return arrayFirst;

        await CompleteDocumentAsync(cancellationToken).ConfigureAwait(false);
        return -1;
    }

    private async ValueTask<int> ReadFirstArrayElementCharacterAsync(CancellationToken cancellationToken)
    {
        if (!_needsArrayElement)
            throw new InvalidOperationException("The JSON document reader is not positioned at an array element.");

        _needsArrayElement = false;
        var first = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
        if (first < 0)
            throw Error("The top-level array is incomplete");
        if (first == ']')
            throw Error("A JSON array cannot end with a trailing comma");
        return first;
    }

    private string ReadValue(char first, out int delimiter)
    {
        var builder = new StringBuilder().Append(first);
        delimiter = -1;

        if (first is '{' or '[')
        {
            ReadComposite(builder);
            return builder.ToString();
        }

        if (first == '"')
        {
            ReadString(builder);
            return builder.ToString();
        }

        while (true)
        {
            var next = ReadCharacter();
            if (next < 0)
                return builder.ToString();
            if (IsJsonWhitespace((char)next) || next is ',' or ']')
            {
                delimiter = next;
                return builder.ToString();
            }
            builder.Append((char)next);
        }
    }

    private async ValueTask<(string Json, int Delimiter)> ReadValueAsync(
        char first,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder().Append(first);

        if (first is '{' or '[')
        {
            await ReadCompositeAsync(builder, cancellationToken).ConfigureAwait(false);
            return (builder.ToString(), -1);
        }

        if (first == '"')
        {
            await ReadStringAsync(builder, cancellationToken).ConfigureAwait(false);
            return (builder.ToString(), -1);
        }

        while (true)
        {
            var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                return (builder.ToString(), -1);
            if (IsJsonWhitespace((char)next) || next is ',' or ']')
                return (builder.ToString(), next);
            builder.Append((char)next);
        }
    }

    private void ReadComposite(StringBuilder builder)
    {
        var stack = new Stack<char>();
        stack.Push(builder[0]);
        var inString = false;
        var escaping = false;

        while (stack.Count > 0)
        {
            var next = ReadCharacter();
            if (next < 0)
                throw Error("The JSON value is incomplete");

            var current = (char)next;
            builder.Append(current);
            if (inString)
            {
                if (escaping)
                    escaping = false;
                else if (current == '\\')
                    escaping = true;
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (current == '"')
                inString = true;
            else if (current is '{' or '[')
                stack.Push(current);
            else if (current is '}' or ']')
            {
                var opening = stack.Pop();
                if ((opening == '{' && current != '}') || (opening == '[' && current != ']'))
                    throw Error($"Unexpected character '{current}'");
            }
        }
    }

    private void ReadString(StringBuilder builder)
    {
        var escaping = false;
        while (true)
        {
            var next = ReadCharacter();
            if (next < 0)
                throw Error("The JSON string is incomplete");

            var current = (char)next;
            builder.Append(current);
            if (escaping)
                escaping = false;
            else if (current == '\\')
                escaping = true;
            else if (current == '"')
                return;
        }
    }

    private async ValueTask ReadCompositeAsync(StringBuilder builder, CancellationToken cancellationToken)
    {
        var stack = new Stack<char>();
        stack.Push(builder[0]);
        var inString = false;
        var escaping = false;

        while (stack.Count > 0)
        {
            var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                throw Error("The JSON value is incomplete");

            var current = (char)next;
            builder.Append(current);
            if (inString)
            {
                if (escaping)
                    escaping = false;
                else if (current == '\\')
                    escaping = true;
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (current == '"')
                inString = true;
            else if (current is '{' or '[')
                stack.Push(current);
            else if (current is '}' or ']')
            {
                var opening = stack.Pop();
                if ((opening == '{' && current != '}') || (opening == '[' && current != ']'))
                    throw Error($"Unexpected character '{current}'");
            }
        }
    }

    private async ValueTask ReadStringAsync(StringBuilder builder, CancellationToken cancellationToken)
    {
        var escaping = false;
        while (true)
        {
            var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0)
                throw Error("The JSON string is incomplete");

            var current = (char)next;
            builder.Append(current);
            if (escaping)
                escaping = false;
            else if (current == '\\')
                escaping = true;
            else if (current == '"')
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
        var next = delimiter;
        if (next < 0 || IsJsonWhitespace((char)next))
            next = ReadNonWhitespace();

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
        var next = delimiter;
        if (next < 0 || IsJsonWhitespace((char)next))
            next = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);

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

    private int ReadNonWhitespace()
    {
        int current;
        do
        {
            current = ReadCharacter();
        }
        while (current >= 0 && IsJsonWhitespace((char)current));
        return current;
    }

    private int ReadCharacter()
    {
        var value = _reader.Read();
        if (value >= 0)
            _position++;
        return value;
    }

    private async ValueTask<int> ReadNonWhitespaceAsync(CancellationToken cancellationToken)
    {
        int current;
        do
        {
            current = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
        }
        while (current >= 0 && IsJsonWhitespace((char)current));
        return current;
    }

    private async ValueTask<int> ReadCharacterAsync(CancellationToken cancellationToken)
    {
        var count = await _reader.ReadAsync(_asyncBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (count == 0)
            return -1;
        _position++;
        return _asyncBuffer[0];
    }

    private InvalidDataException Error(string message)
        => new($"{message} at position {_position}.");

    private static bool IsJsonWhitespace(char value)
        => value is ' ' or '\t' or '\r' or '\n';

    private static bool EndOfFile(out RecordSpan record, out RecordState recordState)
    {
        record = new RecordSpan([], []);
        recordState = RecordState.Eof;
        return true;
    }

    public void Dispose() { }

    private enum DocumentState
    {
        BeforeRoot,
        SingleRoot,
        Array,
        Complete
    }
}
