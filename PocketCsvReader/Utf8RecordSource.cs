using System.Buffers;
using System.Runtime.InteropServices;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;

// Only ASCII syntax is supported. Non-ASCII payload bytes are opaque to the
// shared state machine; all offsets are byte offsets until a field is requested.
internal sealed class Utf8RecordSource : IRecordSource<CsvProfile>, IPositionSource, ICurrentRecordSource
{
    public CsvProfile Profile { get; }
    private readonly Stream _stream;
    private readonly IParser _parser;
    private readonly byte[] _input;
    private readonly int _readSize;
    private readonly List<FieldSpan> _fields = new();
    private readonly FieldMetadataBuffer _publishedFields = new();
    bool ICurrentRecordSource.SupportsCurrentRecord => true;
    private readonly Utf8FieldBuffer _decodedFields = new();
    private readonly SearchValues<byte> _syntax;
    private readonly PositionTracker? _positionTracker;
    private byte[] _record;
    private int _position;
    private int _available;
    private int _length;
    private bool _disposed;
    PositionTracker? IPositionSource.Tracker => _positionTracker;
    public RecordPosition? CurrentPosition => _positionTracker?.CurrentPosition;

    public Utf8RecordSource(Stream stream, CsvProfile profile)
    {
        Profile = profile;
        if (profile.TrackPosition) _positionTracker = new(PositionOffsetUnit.Bytes);
        _stream = stream;
        _parser = FieldParserFactory.Create(profile.Dialect);
        var dialect = profile.Dialect;
        var syntax = dialect.LineTerminator.Select(c => (byte)c).ToList();
        syntax.Add((byte)dialect.Delimiter);
        foreach (char? c in new[] { dialect.QuoteChar, dialect.EscapeChar, dialect.CommentChar, dialect.SkipInitialSpace ? ' ' : (char?)null })
            if (c.HasValue) syntax.Add((byte)c.Value);
        _syntax = SearchValues.Create(syntax.Distinct().ToArray());
        _readSize = profile.ParserOptimizations.BufferSize;
        if (_readSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile), "Buffer size must be positive.");
        _input = ArrayPool<byte>.Shared.Rent(_readSize);
        _record = ArrayPool<byte>.Shared.Rent(_readSize);
    }

    public static bool Supports(CsvProfile profile)
    {
        var d = profile.Dialect;
        return d.Delimiter < 128 && (d.QuoteChar is null or < (char)128)
            && (d.EscapeChar is null or < (char)128) && (d.CommentChar is null or < (char)128)
            && d.LineTerminator.Length > 0 && d.LineTerminator.All(c => c < 128)
            && d.ArrayDelimiter is null && d.ArrayPrefix is null && d.ArraySuffix is null
            && profile.BadDataPolicy is null;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        var result = ReadResult(borrowFields: false);
        record = result.Record.Materialize().AsRecordSpan();
        recordState = result.State;
        return result.IsEndOfFile;
    }

    CurrentRecordResult ICurrentRecordSource.ReadCurrent() => ReadResult(borrowFields: true);

    private CurrentRecordResult ReadResult(bool borrowFields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CurrentRecordResult result;
        while (!TryRead(false, borrowFields, out result))
        {
            _available = _stream.Read(_input, 0, _readSize);
            _position = 0;
            if (_available == 0)
            {
                TryRead(true, borrowFields, out result);
                break;
            }
        }
        return result;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        => (await ReadResultAsync(borrowFields: false, cancellationToken).ConfigureAwait(false)).Materialize();

    ValueTask<CurrentRecordResult> ICurrentRecordSource.ReadCurrentAsync(CancellationToken cancellationToken)
        => ReadResultAsync(borrowFields: true, cancellationToken);

    private async ValueTask<CurrentRecordResult> ReadResultAsync(bool borrowFields, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        CurrentRecordResult result;
        while (!TryRead(false, borrowFields, out result))
        {
            _available = await _stream.ReadAsync(_input.AsMemory(0, _readSize), cancellationToken).ConfigureAwait(false);
            _position = 0;
            if (_available == 0)
            {
                TryRead(true, borrowFields, out result);
                return result;
            }
        }
        return result;
    }

    private bool TryRead(bool eof, bool borrowFields, out CurrentRecordResult result)
    {
        if (_positionTracker is null) return TryReadCore(eof, borrowFields, out result);
        // Keep the grammar and IndexOfAny bulk copies identical in both modes.
        // Only enabled tracking searches the consumed spans for physical newlines.
        int start = _position;
        if (_length == 0) _positionTracker?.BeginRecord();
        bool complete = TryReadCore(eof, borrowFields, out result);
        if (_positionTracker is { } tracker)
        {
            tracker.Advance(_input.AsSpan(start, _position - start));
            if (complete && result.State == RecordState.Record) tracker.CompleteRecord();
        }
        return complete;
    }

    private bool TryReadCore(bool eof, bool borrowFields, out CurrentRecordResult result)
    {
        var fields = _fields;
        int length = _length;
        while (true)
        {
            if (_position == _available)
            {
                if (!eof)
                {
                    _length = length;
                    result = default;
                    return false;
                }
                else
                {
                    if (length == 0)
                    {
                        result = new(true, default, RecordState.Eof);
                        return true;
                    }
                    var end = _parser.ParseEof(length);
                    if (end is ParserState.Record or ParserState.Header)
                        fields.Add(_parser.Result);
                    else if (end != ParserState.Eof)
                        throw new InvalidDataException("Invalid character End-of-File.");
                    result = new(true, PublishRecord(length, borrowFields),
                        end == ParserState.Eof ? RecordState.Eof : RecordState.Record);
                    _parser.Reset(true);
                    _length = 0;
                    fields.Clear();
                    return true;
                }
            }

            byte value = _input[_position];
            // Map payload bytes outside ASCII to one opaque symbol. No decoding
            // or UTF-16 buffer is needed to recognize CSV syntax.
            var state = _parser.Parse(value < 128 ? (char)value : '\uffff', length);
            if (state == ParserState.Reprocess)
                continue;
            if (state == ParserState.Error)
                throw new InvalidDataException($"Invalid byte '{value}' at position {length}.");
            EnsureRecordCapacity(length + 1, length);
            _record[length++] = value;
            _position++;
            if (state == ParserState.Continue && !_syntax.Contains(value))
            {
                // The first payload byte establishes or resumes the value state.
                // Further bytes without CSV syntax cannot change that state.
                // Scan/copy them in bulk, including opaque multibyte UTF-8 text.
                var remaining = _input.AsSpan(_position, _available - _position);
                int run = remaining.IndexOfAny(_syntax);
                if (run < 0) run = remaining.Length;
                EnsureRecordCapacity(checked(length + run), length);
                remaining[..run].CopyTo(_record.AsSpan(length));
                length += run;
                _position += run;
            }
            if (state is ParserState.Field or ParserState.Record or ParserState.Header)
            {
                fields.Add(_parser.Result);
                _parser.Reset(state != ParserState.Field);
            }
            if (state is ParserState.Record or ParserState.Header or ParserState.Comment)
            {
                result = new(false, state == ParserState.Comment ? default
                    : PublishRecord(length, borrowFields),
                    state == ParserState.Comment ? RecordState.Comment : RecordState.Record);
                _parser.Reset(true);
                _length = 0;
                fields.Clear();
                return true;
            }
        }
    }

    private CurrentRecord PublishRecord(int length, bool borrowFields)
    {
        var scratch = CollectionsMarshal.AsSpan(_fields);
        var fields = borrowFields ? _publishedFields.PublishBorrowed(scratch) : FieldMetadataBuffer.CopyOwned(scratch);
        _decodedFields.Reset(length, fields.Length);
        return new CurrentRecord(_record.AsMemory(0, length), fields, _decodedFields);
    }

    private void EnsureRecordCapacity(int capacity, int length)
    {
        if (capacity <= _record.Length) return;
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, checked(_record.Length * 2)));
        _record.AsSpan(0, length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_record);
        _record = larger;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_input);
        ArrayPool<byte>.Shared.Return(_record);
        _decodedFields.Dispose();
        _stream.Dispose();
    }
}
