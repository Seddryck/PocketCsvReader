using System.Buffers;
using System.Runtime.CompilerServices;
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
    bool ICurrentRecordSource.SupportsCurrentRecord => true;
    private readonly Utf8FieldBuffer _decodedFields = new();
    private readonly SearchValues<byte> _syntax;
    private readonly Utf8BlockScanner? _blockScanner;
    private readonly PositionTracker? _positionTracker;
    private byte[]? _spill;
    private int _position;
    private int _available;
    private int _spilledLength;
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
        _blockScanner = Utf8BlockScanner.Create(dialect, _readSize);
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
            _blockScanner?.Reset();
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
            _blockScanner?.Reset();
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
        if (_positionTracker is null) return TryReadRecord(eof, borrowFields, out result);
        // Tracking observes the bytes consumed by either scanner.
        // Only enabled tracking searches the consumed spans for physical newlines.
        int start = _position;
        if (_spilledLength == 0) _positionTracker.BeginRecord();
        bool complete = TryReadRecord(eof, borrowFields, out result);
        if (_positionTracker is { } tracker)
        {
            tracker.Advance(_input.AsSpan(start, _position - start));
            if (complete && result.State == RecordState.Record) tracker.CompleteRecord();
        }
        return complete;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadRecord(bool eof, bool borrowFields, out CurrentRecordResult result)
    {
        if (_spilledLength == 0 && _blockScanner is { } scanner
            && scanner.TryRead(_input.AsMemory(0, _available), ref _position, _decodedFields, out var record))
        {
            result = new(false, record, RecordState.Record);
            return true;
        }

        return TryReadCore(eof, borrowFields, out result);
    }

    private bool TryReadCore(bool eof, bool borrowFields, out CurrentRecordResult result)
    {
        var fields = _fields;
        int start = _position;
        int offset = _spilledLength - start;
        int position = start;
        var input = _input.AsSpan(0, _available);
        while (true)
        {
            if (position == input.Length)
            {
                _position = position;
                if (!eof)
                {
                    // Save this unfinished segment before the next refill overwrites it.
                    AppendToSpill(start);
                    result = default;
                    return false;
                }
                else
                {
                    if (_spilledLength == 0)
                    {
                        result = new(true, default, RecordState.Eof);
                        return true;
                    }
                    var end = _parser.ParseEof(_spilledLength);
                    if (end is ParserState.Record or ParserState.Header)
                        fields.Add(_parser.Result);
                    else if (end != ParserState.Eof)
                        throw new InvalidDataException("Invalid character End-of-File.");
                    result = new(true, PublishRecord(start, borrowFields),
                        end == ParserState.Eof ? RecordState.Eof : RecordState.Record);
                    _parser.Reset(true);
                    _spilledLength = 0;
                    fields.Clear();
                    return true;
                }
            }

            byte value = input[position];
            // Map payload bytes outside ASCII to one opaque symbol. No decoding
            // or UTF-16 buffer is needed to recognize CSV syntax.
            var state = _parser.Parse(value < 128 ? (char)value : '\uffff', checked(position + offset));
            if (state == ParserState.Reprocess)
                continue;
            if (state == ParserState.Error)
            {
                _position = position;
                throw new InvalidDataException($"Invalid byte '{value}' at position {position + offset}.");
            }
            position++;
            if (state == ParserState.Continue && !_syntax.Contains(value))
            {
                // The first payload byte establishes or resumes the value state.
                // Further bytes without CSV syntax cannot change that state.
                // Skip them in bulk, including opaque multibyte UTF-8 text.
                var remaining = input[position..];
                int run = remaining.IndexOfAny(_syntax);
                position += run < 0 ? remaining.Length : run;
            }
            if (state is ParserState.Field or ParserState.Record or ParserState.Header)
            {
                fields.Add(_parser.Result);
                _parser.Reset(state != ParserState.Field);
            }
            if (state is ParserState.Record or ParserState.Header or ParserState.Comment)
            {
                _position = position;
                result = new(false, state == ParserState.Comment ? default
                    : PublishRecord(start, borrowFields),
                    state == ParserState.Comment ? RecordState.Comment : RecordState.Record);
                _parser.Reset(true);
                _spilledLength = 0;
                fields.Clear();
                return true;
            }
        }
    }

    private CurrentRecord PublishRecord(int start, bool borrowFields)
    {
        // Both views are valid until the next read. Refill may overwrite the
        // input chunk, and another spanning record may overwrite the spill.
        ReadOnlyMemory<byte> record = _spilledLength == 0
            ? _input.AsMemory(start, _position - start)
            : CompleteSpilledRecord(start);
        var scratch = CollectionsMarshal.AsSpan(_fields);
        var fields = borrowFields ? _decodedFields.PublishedFields.PublishBorrowed(scratch) : FieldMetadataBuffer.CopyOwned(scratch);
        _decodedFields.Reset(record.Length, fields.Length);
        return new CurrentRecord(record, fields, _decodedFields);
    }

    private ReadOnlyMemory<byte> CompleteSpilledRecord(int start)
    {
        AppendToSpill(start);
        return _spill.AsMemory(0, _spilledLength);
    }

    private void AppendToSpill(int start)
    {
        int count = _position - start;
        if (count == 0)
            return;

        int required = checked(_spilledLength + count);
        if (_spill is null || required > _spill.Length)
        {
            var larger = ArrayPool<byte>.Shared.Rent(Math.Max(required, _spill is null ? _readSize : checked(_spill.Length * 2)));
            if (_spill is not null)
            {
                _spill.AsSpan(0, _spilledLength).CopyTo(larger);
                ArrayPool<byte>.Shared.Return(_spill);
            }
            _spill = larger;
        }

        _input.AsSpan(start, count).CopyTo(_spill.AsSpan(_spilledLength));
        _spilledLength = required;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_input);
        if (_spill is not null)
            ArrayPool<byte>.Shared.Return(_spill);
        _blockScanner?.Dispose();
        _decodedFields.Dispose();
        _stream.Dispose();
    }
}
