using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PocketCsvReader;
public class RecordParser : IDisposable
{
    public CsvProfile Profile { get; }
    protected CharParser CharParser { get; }
    protected IBufferReader Reader { get; }
    protected ReadOnlyMemory<char> Buffer { get; private set; }
    protected ArrayPool<char>? Pool { get; }

    private int? FieldsCount { get; set; }
    public RecordParser(StreamReader reader, CsvProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, CsvProfile profile, ArrayPool<char>? pool)
        : this(profile, profile.ParserOptimizations.ReadAhead
                    ? new DoubleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
                    : new SingleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
              , pool)
    { }

    protected RecordParser(CsvProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        => (Profile, Reader, CharParser) = (profile, buffer, new(profile));

    internal bool ReadNextArray(out string?[]? value)
    {
        var eof = ReadNextRecord(out RecordSpan rawRecord);
        var arrayStringMapper = new SpanMapper<string?[]>((span, fieldSpans) =>
        {
            var values = new string?[fieldSpans.Count()];
            var index = 0;
            foreach (var fieldSpan in fieldSpans)
            {
                var value = span.Slice(fieldSpan.Start, fieldSpan.Length);
                values[index++] = value.Length == 0 ? null : value.ToString();
            }
            return values;
        });
        value = rawRecord.FieldSpans.Length == 0 ? null : arrayStringMapper(rawRecord.Span, rawRecord.FieldSpans);
        return eof;
    }

    public virtual bool ReadNextRecord(out RecordSpan record)
    {
        var index = 0;
        var eof = false;
        var fieldList = new List<FieldSpan>(FieldsCount ?? 20);
        var longSpan = Span<char>.Empty;

        if (Buffer.Length == 0)
        {
            if (!Reader.IsEof)
                Buffer = Reader.Read();
            eof = Buffer.Length == 0;
        }

        var span = Buffer.Span;
        var bufferSize = span.Length;

        while (!eof && index < bufferSize)
        {
            char c = span[index];
            var state = CharParser.Parse(c);
            if (state == ParserState.Field || state == ParserState.Record)
            {
                fieldList.Add(new FieldSpan(CharParser.FieldStart, CharParser.FieldLength, CharParser.IsEscapedField, CharParser.IsQuotedField));

                if (state == ParserState.Record)
                {
                    CharParser.Reset();
                    Buffer = Buffer.Slice(index + 1);
                    FieldsCount ??= fieldList.Count;
                    record =  new RecordSpan(
                        Profile
                        , longSpan.Length > 0 ? (ReadOnlySpan<char>)(longSpan.Concat(span)) : span
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
                    longSpan = longSpan.Concat(span, Pool);

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

        switch (CharParser.ParseEof())
        {
            case ParserState.Record:
                fieldList.Add(new FieldSpan(CharParser.FieldStart, CharParser.FieldLength, CharParser.IsEscapedField, CharParser.IsQuotedField));
                record = new RecordSpan(
                        Profile
                        , longSpan.Length > 0 ? (ReadOnlySpan<char>)(longSpan.Concat(span)) : span
                        , [.. fieldList]);
                return true;
            case ParserState.Eof:
                record = new RecordSpan(Profile, [], []);
                return true;
            case ParserState.Error:
                throw new InvalidDataException($"Invalid character End-of-File.");
            default:
                throw new InvalidOperationException($"Invalid state at end-of-file.");
        }
    }

    public virtual string[] ReadHeaders()
    {
        var headerMapper = new SpanMapper<string[]>((span, fieldSpans) =>
        {
            var headers = new string[fieldSpans.Count()];
            var index = 0;
            foreach (var fieldSpan in fieldSpans)
                headers[index++] = span.Slice(fieldSpan.Start, fieldSpan.Length).ToString();
            return headers;
        });

        var unnamedFieldIndex = -1;
        ReadNextRecord(out RecordSpan rawRecord);
        var fields = rawRecord.FieldSpans.Length == 0 ? [] : headerMapper(rawRecord.Span, rawRecord.FieldSpans);
        return fields.Select(value =>
                {
                    unnamedFieldIndex++;
                    return string.IsNullOrWhiteSpace(value) || !Profile.Descriptor.Header
                        ? $"field_{unnamedFieldIndex}"
                        : value!;
                }).ToArray();
    }

    public int? CountRecords()
    {
        if (!Profile.ParserOptimizations.RowCountAtStart)
            return null;

        var count = CountRecordSeparators();
        count -= Convert.ToInt16(Profile.Descriptor.Header);

        CharParser.Reset();
        Reader.Reset();
        return count;
    }

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

            if (CharParser.Parse(span[index]) == ParserState.Record)
                count++;
            index++;
        }
        if (CharParser.ParseEof() == ParserState.Record)
            count++;

        return count;
    }

    public virtual string GetFirstRecord()
    {
        var longSpan = Span<char>.Empty;
        var span = ReadOnlySpan<char>.Empty;
        var index = 0;
        var bufferSize = 0;

        while (true)
        {
            if (index == bufferSize)
            {
                if (Reader.IsEof)
                    break;
                if (bufferSize > 0)
                    longSpan = longSpan.Concat(span, Pool);
                var buffer = Reader.Read();
                index = 0;
                span = buffer.Span;
                bufferSize = span.Length;
            }

            if (bufferSize == 0)
                break;

            if (CharParser.Parse(span[index]) == ParserState.Record)
            {
                index -= Profile.Descriptor.LineTerminator.Length - 1;
                break;
            }
                
            index++;
        }
        CharParser.Reset();

        if (longSpan.Length == 0)
            return span.Slice(0, index).ToString();
        if (index >= 0)
            return (longSpan.Concat(span.Slice(0, index), Pool)).ToString();
        return longSpan.Slice(0, longSpan.Length + index).ToString();
    }

    public void Dispose()
    {
        Reader.Dispose();
    }
}
