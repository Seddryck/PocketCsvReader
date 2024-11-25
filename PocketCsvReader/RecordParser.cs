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
    protected SpanMapper<string?[]> SpanMapper { get; }

    private int? FieldsCount { get; set; }

    public RecordParser(StreamReader reader, CsvProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, CsvProfile profile, ArrayPool<char>? pool)
        : this(profile, profile.ParserOptimizations.ReadAhead
                    ? new DoubleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
                    : new SingleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
              , pool
              )
    { }

    protected RecordParser(CsvProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        => (Profile, Reader, SpanMapper, CharParser) = (profile, buffer, new ArrayOfStringMapper(profile, pool ?? ArrayPool<char>.Shared).Map, new(profile));

    public virtual bool ReadNextRecord(out string?[] fields)
    {
        var index = 0;
        var eof = false;
        var fieldSpans = new List<FieldSpan>(FieldsCount ?? 20);
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
                fieldSpans.Add(new FieldSpan(CharParser.FieldStart, CharParser.FieldLength, CharParser.IsEscapedField, CharParser.IsQuotedField));

                if (state == ParserState.Record)
                {
                    CharParser.Reset();
                    Buffer = Buffer.Slice(index + 1);
                    FieldsCount ??= fieldSpans.Count;
                    fields = SpanMapper.Invoke(longSpan.Length > 0 ? (ReadOnlySpan<char>)(longSpan.Concat(span)) : span, fieldSpans);
                    return false;
                }
            }
            else if (state == ParserState.Error)
                throw new InvalidDataException($"Invalid character '{c}' at position {index}.");

            // Handle continuation for fields spanning multiple buffers
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
                    eof = true;
                }
            }
        }

        switch (CharParser.ParseEof())
        {
            case ParserState.Record:
                fieldSpans.Add(new FieldSpan(CharParser.FieldStart, CharParser.FieldLength, CharParser.IsEscapedField, CharParser.IsQuotedField));
                fields = SpanMapper.Invoke(longSpan.Length > 0 ? (ReadOnlySpan<char>)longSpan.Concat(span) : span, fieldSpans);
                return true;
            case ParserState.Eof:
                fields = [];
                return true;
            case ParserState.Error:
                throw new InvalidDataException($"Invalid character End-of-File.");
            default:
                throw new InvalidOperationException($"Invalid state at end-of-file.");
        }
    }

    public int? CountRecords()
    {
        if (!Profile.ParserOptimizations.RowCountAtStart)
            return null;

        var count = CountRecordSeparators();
        count -= Convert.ToInt16(Profile.Descriptor.Header);

        Reader.Reset();
        return count;
    }

    protected virtual int CountRecordSeparators()
    {
        int i = 0;
        int n = 0;
        int j = 0;
        bool separatorAtEnd = false;
        bool isCommentLine = false;
        bool isFirstCharOfLine = true;

        do
        {
            var span = Reader.Read().Span;
            n = span.Length;
            if (n > 0 && i == 0)
                i = 1;

            foreach (var c in span)
            {
                if (c != '\0')
                {
                    if (c == Profile.Descriptor.CommentChar && isFirstCharOfLine)
                        isCommentLine = true;
                    isFirstCharOfLine = false;

                    separatorAtEnd = false;
                    if (c == Profile.Descriptor.LineTerminator[j])
                    {
                        j++;
                        if (j == Profile.Descriptor.LineTerminator.Length)
                        {
                            if (!isCommentLine)
                                i++;
                            j = 0;
                            separatorAtEnd = true;
                            isCommentLine = false;
                            isFirstCharOfLine = true;
                        }
                    }
                    else
                        j = 0;
                }
            }
        } while (!Reader.IsEof);

        if (separatorAtEnd)
            i -= 1;

        if (isCommentLine)
            i -= 1;

        return i;
    }

    public string GetFirstRecord(StreamReader reader, string recordSeparator, int bufferSize)
    {
        int i = 0;
        int j = 0;
        Span<char> longRecord = stackalloc char[0];

        var found = false;
        var array = Pool?.Rent(Profile.ParserOptimizations.BufferSize) ?? new char[Profile.ParserOptimizations.BufferSize];
        while (!found)
        {
            var buffer = new Span<char>(array);
            var n = reader.ReadBlock(buffer);
            buffer = buffer.Slice(0, n);
            if (n == 0)
                found = true;

            foreach (var c in buffer)
            {
                i++;
                if (c == '\0')
                    found = true;
                else if (c == recordSeparator[j])
                {
                    j++;
                    if (j == recordSeparator.Length)
                        found = true;
                }
                else
                    j = 0;

                if (found)
                    break;
            }

            if (longRecord.Length == 0)
                longRecord = buffer.Slice(0, i);
            else
            {
                var newArray = Pool?.Rent(longRecord.Length + i) ?? new char[longRecord.Length + i];
                var newSpan = newArray.AsSpan().Slice(0, longRecord.Length + i);
                longRecord.CopyTo(newSpan);
                buffer.CopyTo(newSpan.Slice(longRecord.Length));
                longRecord = newSpan;
                Pool?.Return(newArray);
            }
            i = 0;
        }

        if (array is not null)
            Pool?.Return(array);
        return longRecord.ToString();
    }

    public virtual string[] ReadHeaders()
    {
        var unnamedFieldIndex = -1;
        ReadNextRecord(out var fields);
        return fields.Select(value =>
                {
                    unnamedFieldIndex++;
                    return string.IsNullOrWhiteSpace(value) || !Profile.Descriptor.Header
                        ? $"field_{unnamedFieldIndex}"
                        : value!;
                }).ToArray();
    }

    public void Dispose()
    {
        Reader.Dispose();
    }
}
