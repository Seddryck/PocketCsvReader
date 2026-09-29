using System;
using System.Buffers;
using System.Data;
using System.Linq;
using System.Text;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public abstract class BaseRecordParser<P> : IRecordSource<P>
{
    public P Profile { get; }
    protected IParser FieldParser { get; }
    protected IBufferReader Reader { get; }
    protected ReadOnlyMemory<char> Buffer { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    private int? FieldsCount { get; set; }
    private FieldSpan[] _fieldBuffer = new FieldSpan[20];
    private long _recordNumber;
    private int _errorCount;

    /// <summary>
        /// Initializes a new instance of the <see cref="BaseRecordParser{P}"/> class with the specified parsing profile, buffer reader, optional character pool, and parser factory.
        /// </summary>
        /// <param name="profile">The parsing profile used to configure parsing behavior.</param>
        /// <param name="buffer">The buffer reader supplying character data for parsing.</param>
        /// <param name="pool">An optional array pool for efficient character buffer management.</param>
        /// <param name="parserFactory">A factory function that creates an <see cref="IParser"/> instance based on the provided profile.</param>
        protected BaseRecordParser(P profile, IBufferReader buffer, ArrayPool<char>? pool, Func<P, IParser> parserFactory)
        => (Profile, Reader, Pool, FieldParser) = (profile, buffer, pool, parserFactory(profile));

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
        var index = 0;
        var eof = false;
        var fieldCount = 0;
        EnsureFieldCapacity(FieldsCount ?? 20);
        var longMemory = ReadOnlyMemory<char>.Empty;
        var longSpanLength = 0;

        if (Buffer.Length == 0)
        {
            if (!Reader.IsEof)
                Buffer = Reader.Read();
            eof = Buffer.Length == 0;
        }

        if (eof)
        {
            record = new();
            recordState = RecordState.Eof;
            return true;
        }

        var span = Buffer.Span;
        var bufferSize = span.Length;

        while (!eof && index < bufferSize)
        {
            char c = span[index];
            var state = FieldParser.Parse(c, index + longSpanLength);
            if (state == ParserState.Reprocess)
                continue;
            if (state == ParserState.Field || state == ParserState.Record || state == ParserState.Header)
            {
                AddField(ref fieldCount, FieldParser.Result);
                FieldParser.Reset(state != ParserState.Field);

                if (state == ParserState.Record || state == ParserState.Header)
                {
                    var recordBuffer = Buffer;
                    Buffer = Buffer.Slice(index + 1);
                    FieldsCount ??= fieldCount;
                    record = CreateRecordSpan(
                        longMemory.Length > 0 ? Concat(longMemory, recordBuffer) : recordBuffer
                        , CopyFields(fieldCount));
                    recordState = RecordState.Record;
                    _recordNumber++;
                    return false;
                }
            }
            else if (state == ParserState.Comment)
            {
                FieldParser.Reset(true);
                Buffer = Buffer.Slice(index + 1);
                record = new();
                recordState = RecordState.Comment;
                return false;
            }
            else if (state == ParserState.Error)
            {
                var exception = new InvalidDataException($"Invalid character '{c}' at position {index}.");
                var action = GetBadDataAction(exception, c.ToString(), index + longSpanLength, fieldCount);
                if (action == BadDataAction.Stop)
                {
                    Buffer = ReadOnlyMemory<char>.Empty;
                    record = new();
                    recordState = RecordState.Eof;
                    return true;
                }
                if (action == BadDataAction.SkipRecord)
                {
                    var reachedEof = SkipMalformedRecord(index + 1);
                    FieldParser.Reset(true);
                    record = new();
                    recordState = reachedEof ? RecordState.Eof : RecordState.Comment;
                    return reachedEof;
                }
                if (action != BadDataAction.ReturnPartialRecord)
                    throw exception;
            }

            // Handle continuation for value spanning multiple buffers
            if (++index == bufferSize)
            {
                if (state == ParserState.Continue || state == ParserState.Field)
                {
                    longMemory = Concat(longMemory, Buffer);
                    longSpanLength = longMemory.Length;
                }

                if (!Reader.IsEof)
                {
                    Buffer = Reader.Read();
                    bufferSize = Buffer.Length;
                    span = Buffer.Span;
                    eof = bufferSize == 0;
                    index = 0;
                }
                else
                {
                    bufferSize = 0;
                    span = ReadOnlySpan<char>.Empty;
                    eof = true;
                }
            }
        }

        switch (FieldParser.ParseEof(longMemory.Length))
        {
            case ParserState.Header:
            case ParserState.Record:
                AddField(ref fieldCount, FieldParser.Result);
                record = CreateRecordSpan(
                        longMemory.Length > 0 ? Concat(longMemory, Buffer) : Buffer
                        , CopyFields(fieldCount));
                recordState = RecordState.Record;
                _recordNumber++;
                return true;
            case ParserState.Eof:
                record = CreateRecordSpan(ReadOnlyMemory<char>.Empty, []);
                recordState = RecordState.Eof;
                return true;
            case ParserState.Error:
                throw new InvalidDataException($"Invalid character End-of-File.");
            default:
                throw new InvalidOperationException($"Invalid state at end-of-file.");
        }
    }

    /// <summary>
        /// Creates a <see cref="RecordSpan"/> from the specified character span and array of field spans.
        /// </summary>
        /// <param name="span">The span of characters representing the entire record.</param>
        /// <param name="fields">The array of parsed field spans within the record.</param>
        /// <returns>A <see cref="RecordSpan"/> containing the provided span and fields.</returns>
        protected virtual RecordSpan CreateRecordSpan(ReadOnlyMemory<char> memory, FieldSpan[] fields)
        => RecordSpan.FromMemory(memory, fields);

    private static ReadOnlyMemory<char> Concat(ReadOnlyMemory<char> left, ReadOnlyMemory<char> right)
    {
        var result = new char[left.Length + right.Length];
        left.Span.CopyTo(result);
        right.Span.CopyTo(result.AsSpan(left.Length));
        return result;
    }

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
    {
        var fields = new FieldSpan[count];
        _fieldBuffer.AsSpan(0, count).CopyTo(fields);
        return fields;
    }

    private BadDataAction GetBadDataAction(Exception exception, string offendingInput, long offset, int fieldIndex)
    {
        var policy = (Profile as CsvProfile)?.BadDataPolicy;
        if (policy is null)
            return BadDataAction.Throw;
        if (++_errorCount > policy.MaximumErrors)
            throw new InvalidDataException($"The maximum bad-data count of {policy.MaximumErrors} was exceeded.", exception);
        return policy.Handler(new BadDataContext(_recordNumber + 1, _recordNumber + 1, fieldIndex,
            offset, ParserState.Error, offendingInput, exception));
    }

    private bool SkipMalformedRecord(int startIndex)
    {
        var terminator = (Profile as CsvProfile)!.Dialect.LineTerminator;
        var candidate = Buffer;
        var index = startIndex;
        var matched = 0;
        while (true)
        {
            while (index < candidate.Length)
            {
                var c = candidate.Span[index++];
                matched = c == terminator[matched] ? matched + 1 : c == terminator[0] ? 1 : 0;
                if (matched == terminator.Length)
                {
                    Buffer = candidate.Slice(index);
                    _recordNumber++;
                    return false;
                }
            }
            if (Reader.IsEof)
            {
                Buffer = ReadOnlyMemory<char>.Empty;
                _recordNumber++;
                return true;
            }
            candidate = Reader.Read();
            Buffer = candidate;
            index = 0;
        }
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
        }
        _disposed = true;
    }
}
