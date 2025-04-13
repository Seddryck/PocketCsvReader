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

    protected BaseRecordParser(P profile, IBufferReader buffer, ArrayPool<char>? pool, Func<P, IParser> parserFactory)
        => (Profile, Reader, Pool, FieldParser) = (profile, buffer, pool, parserFactory(profile));

    public virtual bool IsEndOfFile(out RecordSpan record)
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
                    return false;
                }
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
                return true;
            case ParserState.Eof:
                record = CreateRecordSpan([], []);
                return true;
            case ParserState.Error:
                throw new InvalidDataException($"Invalid character End-of-File.");
            default:
                throw new InvalidOperationException($"Invalid state at end-of-file.");
        }
    }

    protected virtual RecordSpan CreateRecordSpan(ReadOnlySpan<char> span, FieldSpan[] fields)
        => new(span, fields);

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

            if (FieldParser.Parse(span[index], index) == ParserState.Record)
                count++;
            index++;
        }
        if (FieldParser.ParseEof(index) == ParserState.Record)
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
