using System;
using System.Buffers;
using System.Data;
using System.Linq;
using System.Text;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public abstract class BaseRecordParser<TProfile> : IRecordSource<TProfile>, IPositionSource, ICurrentRecordSource
{
    public TProfile Profile { get; }
    protected IParser FieldParser { get; }
    protected IBufferReader Reader { get; }
    protected ReadOnlyMemory<char> Buffer { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    private int? FieldsCount { get; set; }
    private readonly FieldMetadataBuffer _publishedFields = new();
    private bool _borrowFields;
    // Unknown subclasses may override public reads or retain their results.
    protected virtual bool SupportsBorrowedRecords => false;
    bool ICurrentRecordSource.SupportsCurrentRecord => SupportsBorrowedRecords;

    CurrentRecordResult ICurrentRecordSource.ReadCurrent()
    {
        _borrowFields = true;
        try
        {
            var eof = IsEndOfFile(out var record, out var state);
            return new(eof, record.Current, state);
        }
        finally { _borrowFields = false; }
    }

    ValueTask<CurrentRecordResult> ICurrentRecordSource.ReadCurrentAsync(CancellationToken cancellationToken)
        => ReadResultAsync(borrowFields: true, cancellationToken);
    private readonly PositionTracker? _position;
    private FieldSpan[] _fieldBuffer = new FieldSpan[20];
    private int _trackedInBuffer;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    private long _recordNumber;
    private int _errorCount;

    /// <summary>
        /// Initializes a new instance of the <see cref="BaseRecordParser{TProfile}"/> class with the specified parsing profile, buffer reader, optional character pool, and parser factory.
        /// </summary>
        /// <param name="profile">The parsing profile used to configure parsing behavior.</param>
        /// <param name="buffer">The buffer reader supplying character data for parsing.</param>
        /// <param name="pool">An optional array pool for efficient character buffer management.</param>
        /// <param name="parserFactory">A factory function that creates an <see cref="IParser"/> instance based on the provided profile.</param>
    protected BaseRecordParser(TProfile profile, IBufferReader buffer, ArrayPool<char>? pool, Func<TProfile, IParser> parserFactory)
    {
        (Profile, Reader, Pool, FieldParser) = (profile, buffer, pool, parserFactory(profile));
        if (profile is IProfile { TrackPosition: true }) _position = new();
    }

    /// <summary>
    /// Attempts to parse the next record from the buffer, indicating whether the end of the file has been reached.
    /// </summary>
    /// <param name="record">When this method returns, contains the parsed record if available, or an empty record if at EOF or a comment line.</param>
    /// <param name="recordState">When this method returns, indicates the type of record parsed: data record, comment, or end-of-file.</param>
    /// <returns>
    /// <c>true</c> if the end of the file has been reached after this call; otherwise, <c>false</c>.
    /// </returns>
    /// <exception cref="InvalidDataException">Thrown if an invalid character or parse error is encountered.</exception>
    /// <exception cref="InvalidOperationException">Thrown if an unexpected parser state occurs at end-of-file.</exception>
    public virtual bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        _position?.BeginRecord();
        _trackedInBuffer = 0;
        var context = default(ReadContext);
        EnsureFieldCapacity(FieldsCount ?? 20);
        bool eof;
        while (!TryParseRecord(ref context, Reader.IsEof, out record, out recordState, out eof))
        {
            Buffer = Reader.Read();
            _trackedInBuffer = 0;
        }
        return eof;
    }

    /// <summary>Reads asynchronously only when the shared character parser needs another buffer.</summary>
    public virtual async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        => (await ReadResultAsync(borrowFields: false, cancellationToken).ConfigureAwait(false)).Materialize();

    private async ValueTask<CurrentRecordResult> ReadResultAsync(bool borrowFields, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _borrowFields = borrowFields;
        try
        {
            _position?.BeginRecord();
            _trackedInBuffer = 0;
            var context = default(ReadContext);
            EnsureFieldCapacity(FieldsCount ?? 20);
            CurrentRecordResult result;
            while (!TryReadResult(ref context, Reader.IsEof, out result))
            {
                Buffer = await Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                _trackedInBuffer = 0;
            }
            return result;
        }
        finally { _borrowFields = false; }
    }

    private bool TryReadResult(ref ReadContext context, bool endOfInput, out CurrentRecordResult result)
    {
        if (!TryParseRecord(ref context, endOfInput, out var record, out var state, out var eof))
        {
            result = default;
            return false;
        }
        result = new(eof, state == RecordState.Record ? record.Current : default, state);
        return true;
    }

    private struct ReadContext
    {
        public int Length;
        public int FieldCount;
        public bool HasInput;
        public bool Skipping;
        public int TerminatorMatched;
    }

    private char[]? _continuation;

    // No I/O or asynchronous mode inside the scanner. The wrappers supply chunks
    // and retain this context only while assembling the current record.
    private bool TryParseRecord(scoped ref ReadContext context, bool endOfInput,
        out RecordSpan record, out RecordState recordState, out bool eof)
    {
        if (_position is null)
            return TryParseRecordCore(ref context, endOfInput, out record, out recordState, out eof);

        var input = Buffer;
        long start = _position.Offset;
        bool complete = TryParseRecordCore(ref context, endOfInput, out record, out recordState, out eof);
        int consumed = complete ? input.Length - Buffer.Length : input.Length;
        // Error diagnostics may already have advanced to the offending input.
        int advanced = checked((int)(_position.Offset - start));
        _position.Advance(input.Span.Slice(advanced, consumed - advanced));
        if (complete && recordState == RecordState.Record) _position.CompleteRecord();
        return complete;
    }

    private bool TryParseRecordCore(scoped ref ReadContext context, bool endOfInput,
        out RecordSpan record, out RecordState recordState, out bool eof)
    {
        record = default;
        recordState = RecordState.Eof;
        eof = false;
        int index = 0;
        int fieldCount = context.FieldCount;
        int length = context.Length;
        bool skipping = context.Skipping;
        int matched = context.TerminatorMatched;
        context.HasInput |= Buffer.Length > 0;
        var span = Buffer.Span;

        while (index < span.Length)
        {
            if (skipping)
            {
                if (!TrySkipMalformedRecord(ref index, ref matched)) break;
                recordState = RecordState.Comment;
                return true;
            }

            char c = span[index];
            var state = FieldParser.Parse(c, index + length);
            if (state == ParserState.Reprocess) continue;
            if (state != ParserState.Continue)
            {
                if (TryCompleteToken(state, c, index, length, ref fieldCount,
                    out var skipRecord, out record, out recordState, out eof))
                    return true;
                skipping = skipRecord;
            }
            index++;
        }

        if (endOfInput)
            return FinishRecord(context.HasInput, length, fieldCount, skipping, out record, out recordState, out eof);

        context.Length = skipping ? length : AppendContinuation(length, Buffer.Span);
        context.FieldCount = fieldCount;
        context.Skipping = skipping;
        context.TerminatorMatched = matched;
        return false;
    }

    private bool TryCompleteToken(ParserState state, char c, int index, int length,
        scoped ref int fieldCount, out bool skipRecord, out RecordSpan record, out RecordState recordState, out bool eof)
    {
        record = default;
        recordState = RecordState.Eof;
        eof = false;
        skipRecord = false;
        if (state is ParserState.Field or ParserState.Record or ParserState.Header)
        {
            AddField(ref fieldCount, FieldParser.Result);
            FieldParser.Reset(state != ParserState.Field);
            if (state == ParserState.Field) return false;
            record = CreateRecordSpan(RecordMemoryFor(length, Buffer.Slice(0, index + 1)), CopyFields(fieldCount));
            Buffer = Buffer.Slice(index + 1);
            FieldsCount ??= fieldCount;
            _recordNumber++;
            recordState = RecordState.Record;
            return true;
        }
        if (state == ParserState.Comment)
        {
            FieldParser.Reset(true);
            Buffer = Buffer.Slice(index + 1);
            recordState = RecordState.Comment;
            return true;
        }
        if (state != ParserState.Error) return false;

        ConsumePosition(index);
        var exception = new InvalidDataException($"Invalid character '{c}' at position {index}.");
        switch (GetBadDataAction(exception, c.ToString(), index + length, fieldCount))
        {
            case BadDataAction.Stop:
                Buffer = ReadOnlyMemory<char>.Empty;
                eof = true;
                return true;
            case BadDataAction.SkipRecord:
                skipRecord = true;
                return false;
            case BadDataAction.ReturnPartialRecord:
                return false;
            default:
                throw exception;
        }
    }

    private void ConsumePosition(int consumed)
    {
        if (_position is null) return;
        _position.Advance(Buffer.Span.Slice(_trackedInBuffer, consumed - _trackedInBuffer));
        _trackedInBuffer = consumed;
    }

    private bool TrySkipMalformedRecord(ref int index, ref int matched)
    {
        var terminator = (Profile as CsvProfile)!.Dialect.LineTerminator;
        var span = Buffer.Span;
        while (index < span.Length)
        {
            char c = span[index++];
            matched = c == terminator[matched] ? matched + 1 : c == terminator[0] ? 1 : 0;
            if (matched != terminator.Length) continue;
            Buffer = Buffer.Slice(index);
            _recordNumber++;
            _position?.SkipRecord();
            FieldParser.Reset(true);
            return true;
        }
        return false;
    }

    private bool FinishRecord(bool hasInput, int length, int fieldCount, bool skipping,
        out RecordSpan record, out RecordState recordState, out bool eof)
    {
        record = default;
        recordState = RecordState.Eof;
        eof = true;
        if (skipping)
        {
            Buffer = ReadOnlyMemory<char>.Empty;
            _recordNumber++;
            _position?.SkipRecord();
            FieldParser.Reset(true);
            return true;
        }
        if (!hasInput) return true;

        switch (FieldParser.ParseEof(length + Buffer.Length))
        {
            case ParserState.Header:
            case ParserState.Record:
                AddField(ref fieldCount, FieldParser.Result);
                record = CreateRecordSpan(RecordMemoryFor(length, Buffer), CopyFields(fieldCount));
                Buffer = ReadOnlyMemory<char>.Empty;
                recordState = RecordState.Record;
                _recordNumber++;
                return true;
            case ParserState.Eof:
                record = CreateRecordSpan(ReadOnlyMemory<char>.Empty, []);
                Buffer = ReadOnlyMemory<char>.Empty;
                return true;
            case ParserState.Error:
                throw new InvalidDataException("Invalid character End-of-File.");
            default:
                throw new InvalidOperationException("Invalid state at end-of-file.");
        }
    }

    private ReadOnlyMemory<char> RecordMemoryFor(int length, ReadOnlyMemory<char> tail)
    {
        if (length == 0) return tail;
        int total = AppendContinuation(length, tail.Span);
        return _continuation.AsMemory(0, total);
    }

    private int AppendContinuation(int length, ReadOnlySpan<char> tail)
    {
        if (tail.IsEmpty) return length;
        int required = checked(length + tail.Length);
        if (_continuation is null || _continuation.Length < required)
        {
            int capacity = Math.Max(required, checked((_continuation?.Length ?? 0) * 2));
            var larger = Pool?.Rent(capacity) ?? new char[capacity];
            if (_continuation is not null)
            {
                _continuation.AsSpan(0, length).CopyTo(larger);
                Pool?.Return(_continuation);
            }
            _continuation = larger;
        }
        tail.CopyTo(_continuation.AsSpan(length));
        return required;
    }

    /// <summary>
        /// Creates a <see cref="RecordSpan"/> from the specified character span and array of field spans.
        /// </summary>
        /// <param name="memory">The span of characters representing the entire record.</param>
        /// <param name="fields">The array of parsed field spans within the record.</param>
        /// <returns>A <see cref="RecordSpan"/> containing the provided span and fields.</returns>
    protected virtual RecordSpan CreateRecordSpan(ReadOnlyMemory<char> memory, FieldSpan[] fields)
        => RecordSpan.FromMemory(memory, fields);

    private void AddField(ref int count, FieldSpan field)
    {
        EnsureFieldCapacity(count + 1);
        _fieldBuffer[count++] = field;
    }

    private void EnsureFieldCapacity(int capacity)
    {
        if (_fieldBuffer.Length >= capacity)
            return;
        Array.Resize(ref _fieldBuffer, Math.Max(capacity, _fieldBuffer.Length * 2));
    }

    private FieldSpan[] CopyFields(int count)
        => _borrowFields ? _publishedFields.PublishBorrowed(_fieldBuffer.AsSpan(0, count))
            : FieldMetadataBuffer.CopyOwned(_fieldBuffer.AsSpan(0, count));

    private BadDataAction GetBadDataAction(Exception exception, string offendingInput, long offset, int fieldIndex)
    {
        var policy = (Profile as CsvProfile)?.BadDataPolicy;
        if (policy is null)
            return BadDataAction.Throw;
        if (++_errorCount > policy.MaximumErrors)
            throw new InvalidDataException($"The maximum bad-data count of {policy.MaximumErrors} was exceeded.", exception);
        var start = _position?.PendingPosition;
        return policy.Handler(new BadDataContext(start?.RecordNumber ?? _recordNumber + 1,
            _position?.PhysicalLine ?? _recordNumber + 1, fieldIndex,
            _position?.Offset ?? offset, ParserState.Error, offendingInput, exception)
        { RecordPosition = start });
    }

    /// <summary>
    /// Counts the number of record separators in the input stream.
    /// </summary>
    /// <returns>The total number of records detected in the input.</returns>
    /// <exception cref="InvalidDataException">Thrown if an invalid character is encountered during parsing.</exception>
    protected virtual int CountRecordSeparators()
    {
        var span = ReadOnlySpan<char>.Empty;
        var index = 0;
        var bufferSize = 0;
        var count = 0;

        while (true)
        {
            if (index == bufferSize)
            {
                if (Reader.IsEof)
                    break;
                var buffer = Reader.Read();
                index = 0;
                span = buffer.Span;
                bufferSize = span.Length;
            }

            if (bufferSize == 0)
                break;
            var state = FieldParser.Parse(span[index], index);
            if (state == ParserState.Reprocess)
                continue;
            switch (state)
            {
                case ParserState.Error:
                    throw new InvalidDataException($"Invalid character '{span[index]}' at position {index}.");
                case ParserState.Field:
                    FieldParser.Reset();
                    break;
                case ParserState.Record:
                    FieldParser.Reset();
                    count++;
                    break;
            }

            index++;
        }
        if (FieldParser.ParseEof(index) == ParserState.Record && !FieldParser.Result.IsEmpty)
            count++;

        return count;
    }

    private bool _disposed;
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            Reader.Dispose();
            if (_continuation is not null) Pool?.Return(_continuation);
        }
        _disposed = true;
    }
}
