using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public abstract class BaseRecordParser<P> : IDisposable
{
    public P Profile { get; }
    protected IParser FieldParser { get; }
    protected IBufferReader Reader { get; }
    protected ReadOnlyMemory<char> Buffer { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    private int? FieldsCount { get; set; }

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
        var fieldList = new List<FieldSpan>(FieldsCount ?? 20);
        var longSpan = Span<char>.Empty;
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
            if (state == ParserState.Field || state == ParserState.Record || state == ParserState.Header)
            {
                fieldList.Add(FieldParser.Result);
                FieldParser.Reset();

                if (state == ParserState.Record || state == ParserState.Header)
                {
                    Buffer = Buffer.Slice(index + 1);
                    FieldsCount ??= fieldList.Count;
                    record = CreateRecordSpan(
                        longSpan.Length > 0 ? (ReadOnlySpan<char>)(longSpan.Concat(span)) : span
                        , [.. fieldList]);
                    recordState = RecordState.Record;
                    return false;
                }
            }
            else if (state == ParserState.Comment)
            {
                FieldParser.Reset();
                Buffer = Buffer.Slice(index + 1);
                record = new();
                recordState = RecordState.Comment;
                return false;
            }
            else if (state == ParserState.Error)
                throw new InvalidDataException($"Invalid character '{c}' at position {index}.");

            // Handle continuation for value spanning multiple buffers
            if (++index == bufferSize)
            {
                if (state == ParserState.Continue || state == ParserState.Field)
                {
                    longSpan = longSpan.Concat(span, Pool);
                    longSpanLength = longSpan.Length;
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

        switch (FieldParser.ParseEof(longSpan.Length))
        {
            case ParserState.Header:
            case ParserState.Record:
                fieldList.Add(FieldParser.Result);
                record = CreateRecordSpan(
                        longSpan.Length > 0 ? (ReadOnlySpan<char>)(longSpan.Concat(span)) : span
                        , [.. fieldList]);
                recordState = RecordState.Record;
                return true;
            case ParserState.Eof:
                record = CreateRecordSpan([], []);
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
        protected virtual RecordSpan CreateRecordSpan(ReadOnlySpan<char> span, FieldSpan[] fields)
        => new(span, fields);

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
            switch (FieldParser.Parse(span[index], index))
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
