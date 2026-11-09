using PocketCsvReader.Json;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

internal sealed class NdjsonRecordSource : IBorrowedRecordSource<NdjsonProfile>, IPositionSource
{
    private readonly BufferedRecordReader _reader;
    private readonly NdjsonFramer _framer;
    private readonly ProjectedNdjsonFramer? _fusedFramer;
    private readonly PositionTracker? _position;
    private ReadOnlyMemory<char> _selectionInput;
    private RecordPosition _selectionStart;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    // The current record owns this buffer until the reader advances to the next record.
    private readonly FieldSpan[]? _projectedFields;
    private readonly JsonFieldBuffer _fieldBuffer = new();
    private readonly ProjectedJsonValueFramer? _projectedValueFramer;
    private readonly StableJsonRecordLayout? _stableLayout;
    private IEnumerator<ReadOnlyMemory<char>>? _matches;

    public NdjsonProfile Profile { get; }

    public NdjsonRecordSource(StreamReader reader, NdjsonProfile profile)
    {
        Profile = profile;
        if (profile.TrackPosition) _position = new();
        _reader = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
        _framer = new NdjsonFramer(profile.Dialect.LineTerminator, profile.Dialect.CommentChar);
        if (profile.StableFieldLayout)
        {
            _stableLayout = new(profile.Dialect.Whitespaces, profile.ProjectedProperties, profile.OrderedProjection);
            return;
        }
        if (profile.ProjectedProperties is not { } properties)
            return;

        _projectedFields = new FieldSpan[properties.Count];
        _projectedValueFramer = new ProjectedJsonValueFramer(properties, _projectedFields, profile.OrderedProjection);
        if (CanFuseProjection(profile))
            _fusedFramer = new(_projectedValueFramer, _framer, profile.Dialect.LineTerminator == "\r\n");
    }

    private static bool CanFuseProjection(NdjsonProfile profile)
        => profile.ParsedRecordPath is null && profile.Dialect.CommentChar is null
            && profile.Dialect.LineTerminator is "\n" or "\r\n"
            && profile.Dialect.Whitespaces.Order().SequenceEqual(new[] { '\t', '\n', '\r', ' ' });

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        while (true)
        {
            if (TrySelectRow(out var selected))
            {
                record = RecordSpan.FromMemory(selected.Memory, selected.Fields);
                recordState = RecordState.Record;
                return false;
            }
            _reader.BeginOperation();
            _position?.BeginRecord();
            var capture = _reader.ReadFrame(_fusedFramer is { } fused ? fused : _framer);
            if (capture is not { } captured)
                return EndOfFile(out record, out recordState);

            var parsed = ParseCapturedRecord(captured.Memory);
            if (parsed is null)
                continue;

            _reader.Protect(captured);
            if (Profile.ParsedRecordPath is not null)
            {
                _selectionInput = parsed.Value.Memory;
                _selectionStart = _position?.PendingPosition ?? default;
                _matches = Profile.ParsedRecordPath.Select(parsed.Value.Memory, Profile.Dialect.Whitespaces, parsed.Value.Fields).GetEnumerator();
                continue;
            }
            _position?.CompleteRecord();
            var isEndOfFile = _reader.Peek() < 0;
            record = RecordSpan.FromMemory(parsed.Value.Memory, parsed.Value.Fields);
            recordState = RecordState.Record;
            return isEndOfFile;
        }
    }

    async ValueTask<CurrentRecordResult> ICurrentRecordSource.ReadCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            if (TrySelectRow(out var selected))
            {
                return new(false, new CurrentRecord(selected.Memory, selected.Fields), RecordState.Record);
            }
            _reader.BeginOperation();
            _position?.BeginRecord();
            var capture = await _reader.ReadFrameAsync(_fusedFramer is { } fused ? fused : _framer, cancellationToken).ConfigureAwait(false);
            if (capture is not { } captured)
                return new(true, default, RecordState.Eof);

            var parsed = ParseCapturedRecord(captured.Memory);
            if (parsed is null)
                continue;

            _reader.Protect(captured);
            if (Profile.ParsedRecordPath is not null)
            {
                _selectionInput = parsed.Value.Memory;
                _selectionStart = _position?.PendingPosition ?? default;
                _matches = Profile.ParsedRecordPath.Select(parsed.Value.Memory, Profile.Dialect.Whitespaces, parsed.Value.Fields).GetEnumerator();
                continue;
            }
            _position?.CompleteRecord();
            var isEndOfFile = await _reader.PeekAsync(cancellationToken).ConfigureAwait(false) < 0;
            return new(
                isEndOfFile,
                new CurrentRecord(parsed.Value.Memory, parsed.Value.Fields),
                RecordState.Record);
        }
    }

    private ParsedRecord? ParseCapturedRecord(ReadOnlyMemory<char> record)
    {
        if (_fusedFramer is null)
            return ParseRecord(record, _framer.CommentIndex);
        if (_fusedFramer.RequiresFallback)
            return ParseRecord(record, -1);
        if (!_fusedFramer.HasValue)
            return null;
        var projectedRecord = record.Slice(_fusedFramer.ValueStart, _fusedFramer.ValueLength);
        MaterializeProjectedValues(projectedRecord);
        return new(projectedRecord, _projectedFields!);
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
            return ParseInputCandidate(candidate);
        }
        catch (InvalidDataException) when (commentIndex >= 0)
        {
            return ParseInputCandidate(record);
        }
    }

    private ParsedRecord ParseInputCandidate(ReadOnlyMemory<char> record)
        => Profile.ParsedRecordPath is null
            ? ParseCandidate(record)
            : new ParsedRecord(record, JsonRecordParser.Parse(record.Span, Profile.Dialect.Whitespaces));

    private bool TrySelectRow(out ParsedRecord row)
    {
        if (_matches?.MoveNext() == true)
        {
            row = ParseCandidate(_matches.Current);
            if (_position is not null)
            {
                if (!_selectionInput.Span.Overlaps(_matches.Current.Span, out int offset))
                    throw new InvalidOperationException("Selected JSON must retain its source memory.");
                _position.CompleteRecordAt(_selectionStart, _selectionInput.Span[..offset]);
            }
            return true;
        }
        _matches?.Dispose();
        _matches = null;
        row = default;
        return false;
    }

    private ParsedRecord ParseCandidate(ReadOnlyMemory<char> record)
    {
        if (_stableLayout is not null)
        {
            var stableRecord = Trim(record);
            return new ParsedRecord(stableRecord, _stableLayout.Parse(stableRecord.Span));
        }

        if (_projectedValueFramer is null)
        {
            return new ParsedRecord(record, JsonRecordParser.Parse(record.Span, Profile.Dialect.Whitespaces,
                buffer: Profile.ParsedRecordPath is null ? _fieldBuffer : null));
        }

        var projectedRecord = Trim(record);
        _projectedValueFramer.Prepare(projectedRecord.Span[0], 0);
        _projectedValueFramer.Reset();
        var result = _projectedValueFramer.Scan(projectedRecord.Span);
        if (!result.Complete)
            _projectedValueFramer.CompleteAtEof();
        if (!IsNullOrWhiteSpace(projectedRecord.Span[result.Consumed..]))
            throw new InvalidDataException($"Unexpected character after the JSON object at position {result.Consumed}.");

        MaterializeProjectedValues(projectedRecord);
        return new ParsedRecord(projectedRecord, _projectedFields!);
    }

    private void MaterializeProjectedValues(ReadOnlyMemory<char> record)
    {
        if (_projectedValueFramer!.RequiresValueMaterialization)
            JsonRecordParser.MaterializeProjectedValues(record.Span, Profile.Dialect.Whitespaces, _projectedFields!);
    }

    private ReadOnlyMemory<char> Trim(ReadOnlyMemory<char> value)
    {
        var start = 0;
        while (start < value.Length && IsWhitespace(value.Span[start]))
            start++;

        var length = value.Length - start;
        while (length > 0 && IsWhitespace(value.Span[start + length - 1]))
            length--;

        return value.Slice(start, length);
    }

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
    {
        _matches?.Dispose();
        _reader.Dispose();
    }

    private readonly record struct ParsedRecord(ReadOnlyMemory<char> Memory, FieldSpan[] Fields);

    internal sealed class NdjsonFramer : IRecordFramer
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

        // Resume at a chunk boundary after projection validation failed. The
        // capture must reach its original line boundary before reporting errors.
        internal void Resume(int total, bool inString, bool escaping, int matched)
        {
            Reset();
            _total = total;
            _inString = inString;
            _escaping = escaping;
            _matched = matched;
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
