using PocketCsvReader.Json;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

internal sealed class NdjsonRecordSource : IRecordSource<NdjsonProfile>
{
    private readonly BufferedRecordReader _reader;
    private readonly NdjsonFramer _framer;
    // The current record owns this buffer until the reader advances to the next record.
    private readonly FieldSpan[]? _projectedFields;
    private readonly ProjectedJsonValueFramer? _projectedValueFramer;
    private StableJsonShape? _stableShape;
    private FieldSpan[]? _stableFields;
    private FieldSpan[]? _stableScratchFields;
    private int[]? _stableProjectedOrdinals;

    public NdjsonProfile Profile { get; }

    public NdjsonRecordSource(StreamReader reader, NdjsonProfile profile)
    {
        Profile = profile;
        _reader = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize);
        _framer = new NdjsonFramer(profile.Dialect.LineTerminator, profile.Dialect.CommentChar);
        _projectedFields = profile.ProjectedProperties is null
            ? null
            : new FieldSpan[profile.ProjectedProperties.Count];
        _projectedValueFramer = profile.ProjectedProperties is null
            ? null
            : new ProjectedJsonValueFramer(
                profile.ProjectedProperties,
                _projectedFields!,
                profile.OrderedProjection);
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
            return ParseCandidate(candidate);
        }
        catch (InvalidDataException) when (commentIndex >= 0)
        {
            return ParseCandidate(record);
        }
    }

    private ParsedRecord ParseCandidate(ReadOnlyMemory<char> record)
    {
        if (Profile.StableObjectShape)
            return ParseStableCandidate(record);

        if (_projectedValueFramer is null)
            return new ParsedRecord(record, JsonRecordParser.Parse(record.Span, Profile.Dialect.Whitespaces));

        var projectedRecord = Trim(record);
        _projectedValueFramer.Prepare(projectedRecord.Span[0], 0);
        _projectedValueFramer.Reset();
        var result = _projectedValueFramer.Scan(projectedRecord.Span);
        if (!result.Complete)
            _projectedValueFramer.CompleteAtEof();
        if (!IsNullOrWhiteSpace(projectedRecord.Span[result.Consumed..]))
            throw new InvalidDataException($"Unexpected character after the JSON object at position {result.Consumed}.");

        if (_projectedValueFramer.RequiresValueMaterialization)
        {
            JsonRecordParser.MaterializeProjectedValues(
                projectedRecord.Span,
                Profile.Dialect.Whitespaces,
                _projectedFields!);
        }

        return new ParsedRecord(projectedRecord, _projectedFields!);
    }

    private ParsedRecord ParseStableCandidate(ReadOnlyMemory<char> record)
    {
        var stableRecord = Trim(record);
        FieldSpan[] fields;
        if (_stableFields is null)
        {
            if (stableRecord.IsEmpty || stableRecord.Span[0] != '{')
                throw new InvalidDataException("A stable-shape NDJSON record must be a JSON object.");
            fields = JsonRecordParser.Parse(stableRecord.Span, Profile.Dialect.Whitespaces);
            _stableShape = JsonRecordParser.CreateStableShape(stableRecord.Span, fields);
            _stableFields = fields;
            if (Profile.ProjectedProperties is not null)
                _stableProjectedOrdinals = ResolveProjectedOrdinals(stableRecord.Span, fields);
        }
        else
        {
            var stableShape = _stableShape
                ?? throw new InvalidOperationException("The stable JSON shape was not initialized.");
            _stableScratchFields ??= stableShape.CreateFieldBuffer();
            fields = JsonRecordParser.ParseStableObjectValues(
                stableRecord.Span,
                Profile.Dialect.Whitespaces,
                stableShape,
                _stableScratchFields);
            (_stableFields, _stableScratchFields) = (fields, _stableFields);
        }

        if (_stableProjectedOrdinals is null)
            return new ParsedRecord(stableRecord, fields);

        for (var projectedOrdinal = 0; projectedOrdinal < _stableProjectedOrdinals.Length; projectedOrdinal++)
            _projectedFields![projectedOrdinal] = fields[_stableProjectedOrdinals[projectedOrdinal]];
        return new ParsedRecord(stableRecord, _projectedFields!);
    }

    private int[] ResolveProjectedOrdinals(ReadOnlySpan<char> record, FieldSpan[] fields)
    {
        var ordinals = new int[Profile.ProjectedProperties!.Count];
        var previousOrdinal = -1;
        for (var projectedOrdinal = 0; projectedOrdinal < ordinals.Length; projectedOrdinal++)
        {
            var property = Profile.ProjectedProperties[projectedOrdinal];
            var sourceOrdinal = FindProperty(record, fields, property);
            if (sourceOrdinal < 0)
                throw new InvalidDataException($"Projected property '{property}' was not found.");
            if (Profile.OrderedProjection && sourceOrdinal <= previousOrdinal)
                throw new InvalidDataException($"Projected property '{property}' was not found in the configured order.");
            ordinals[projectedOrdinal] = sourceOrdinal;
            previousOrdinal = sourceOrdinal;
        }
        return ordinals;
    }

    private static int FindProperty(ReadOnlySpan<char> record, FieldSpan[] fields, string property)
    {
        for (var ordinal = 0; ordinal < fields.Length; ordinal++)
        {
            var field = fields[ordinal];
            if (field.DecodedLabel is not null
                ? field.DecodedLabel.Equals(property, StringComparison.Ordinal)
                : record.Slice(field.Label.Start, field.Label.Length).SequenceEqual(property))
            {
                return ordinal;
            }
        }
        return -1;
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
