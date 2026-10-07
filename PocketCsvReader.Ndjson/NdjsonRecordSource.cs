using PocketCsvReader.Json;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

internal sealed class NdjsonRecordSource : IRecordSource<NdjsonProfile>
{
    private readonly BufferedRecordReader _reader;
    private readonly NdjsonFramer _framer;
    // The current record owns this buffer until the reader advances to the next record.
    private readonly FieldSpan[]? _projectedFields;

    public NdjsonProfile Profile { get; }

    public NdjsonRecordSource(StreamReader reader, NdjsonProfile profile)
    {
        Profile = profile;
        _reader = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize);
        _framer = new NdjsonFramer(profile.Dialect.LineTerminator, profile.Dialect.CommentChar);
        _projectedFields = profile.ProjectedProperties is null
            ? null
            : new FieldSpan[profile.ProjectedProperties.Count];
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        while (true)
        {
            _reader.BeginOperation();
            var capture = _reader.ReadFrame(_framer);
            if (capture is not { } captured)
                return EndOfFile(out record, out recordState);

            var parsed = ParseRecord(captured.Memory, _framer.CommentIndex);
            if (parsed is null)
                continue;

            _reader.Protect(captured);
            var isEndOfFile = _reader.Peek() < 0;
            record = RecordSpan.FromMemory(parsed.Value.Memory, parsed.Value.Fields);
            recordState = RecordState.Record;
            return isEndOfFile;
        }
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            _reader.BeginOperation();
            var capture = await _reader.ReadFrameAsync(_framer, cancellationToken).ConfigureAwait(false);
            if (capture is not { } captured)
                return new(true, RecordMemory.Empty, RecordState.Eof);

            var parsed = ParseRecord(captured.Memory, _framer.CommentIndex);
            if (parsed is null)
                continue;

            _reader.Protect(captured);
            var isEndOfFile = await _reader.PeekAsync(cancellationToken).ConfigureAwait(false) < 0;
            return new(
                isEndOfFile,
                new RecordMemory(parsed.Value.Memory, parsed.Value.Fields),
                RecordState.Record);
        }
    }

    private ParsedRecord? ParseRecord(ReadOnlyMemory<char> record, int commentIndex)
    {
        var candidate = commentIndex >= 0
            ? TrimEnd(record[..commentIndex])
            : record;

        if (IsNullOrWhiteSpace(candidate.Span))
            return null;

        try
        {
            return new ParsedRecord(
                candidate,
                ParseFields(candidate.Span));
        }
        catch (InvalidDataException) when (commentIndex >= 0)
        {
            return new ParsedRecord(
                record,
                ParseFields(record.Span));
        }
    }

    private FieldSpan[] ParseFields(ReadOnlySpan<char> record)
        => JsonRecordParser.Parse(
            record,
            Profile.Dialect.Whitespaces,
            Profile.ProjectedProperties,
            _projectedFields);

    private ReadOnlyMemory<char> TrimEnd(ReadOnlyMemory<char> value)
    {
        var length = value.Length;
        while (length > 0 && IsWhitespace(value.Span[length - 1]))
            length--;
        return value[..length];
    }

    private bool IsNullOrWhiteSpace(ReadOnlySpan<char> value)
    {
        foreach (var current in value)
        {
            if (!IsWhitespace(current))
                return false;
        }
        return true;
    }

    private bool IsWhitespace(char value)
        => Array.IndexOf(Profile.Dialect.Whitespaces, value) >= 0;

    private static bool EndOfFile(out RecordSpan record, out RecordState recordState)
    {
        record = new RecordSpan([], []);
        recordState = RecordState.Eof;
        return true;
    }

    public void Dispose()
        => _reader.Dispose();

    private readonly record struct ParsedRecord(ReadOnlyMemory<char> Memory, FieldSpan[] Fields);

    private sealed class NdjsonFramer : IRecordFramer
    {
        private readonly string _terminator;
        private readonly char? _commentChar;
        private readonly int[] _failure;
        private int _matched;
        private int _total;
        private bool _inString;
        private bool _escaping;
        private bool _inComment;

        public int CommentIndex { get; private set; }

        public NdjsonFramer(string terminator, char? commentChar)
        {
            if (string.IsNullOrEmpty(terminator))
                throw new InvalidOperationException("The line terminator cannot be empty.");

            _terminator = terminator;
            _commentChar = commentChar;
            _failure = BuildFailureTable(terminator);
        }

        public void Reset()
        {
            _matched = 0;
            _total = 0;
            _inString = false;
            _escaping = false;
            _inComment = false;
            CommentIndex = -1;
        }

        public FrameScanResult Scan(ReadOnlySpan<char> input)
        {
            for (var index = 0; index < input.Length; index++)
            {
                var current = input[index];
                var wasInString = _inString;
                if (!_inComment)
                {
                    UpdateStringState(current);
                    if (!wasInString && !_inString && current == _commentChar)
                    {
                        _inComment = true;
                        CommentIndex = _total;
                    }
                }

                _total++;
                if (!CanTerminate(wasInString))
                {
                    _matched = 0;
                    continue;
                }

                if (AdvanceTerminator(current))
                    return new FrameScanResult(index + 1, true, _total - _terminator.Length);
            }

            return new FrameScanResult(input.Length, false);
        }

        public FrameScanResult CompleteAtEof()
            => _total == 0
                ? default
                : new FrameScanResult(0, true, _total);

        private bool CanTerminate(bool wasInString)
            => _inComment || (!wasInString && !_inString);

        private bool AdvanceTerminator(char current)
        {
            while (_matched > 0 && current != _terminator[_matched])
                _matched = _failure[_matched - 1];
            if (current == _terminator[_matched])
                _matched++;

            return _matched == _terminator.Length;
        }

        private void UpdateStringState(char current)
        {
            if (!_inString)
            {
                if (current == '"')
                    _inString = true;
                return;
            }

            if (_escaping)
                _escaping = false;
            else if (current == '\\')
                _escaping = true;
            else if (current == '"')
                _inString = false;
        }

        private static int[] BuildFailureTable(string value)
        {
            var failure = new int[value.Length];
            for (var index = 1; index < value.Length; index++)
            {
                var candidate = failure[index - 1];
                while (candidate > 0 && value[index] != value[candidate])
                    candidate = failure[candidate - 1];
                if (value[index] == value[candidate])
                    candidate++;
                failure[index] = candidate;
            }
            return failure;
        }
    }
}
