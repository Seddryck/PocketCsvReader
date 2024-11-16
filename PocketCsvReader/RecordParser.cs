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
    protected FieldParser FieldParser { get; }
    protected IBufferReader Reader { get; }
    protected ReadOnlyMemory<char> Buffer { get; private set; }
    protected ArrayPool<char>? Pool { get; }

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
        => (Profile, Reader, FieldParser) = (profile, buffer, new FieldParser(profile, pool ?? ArrayPool<char>.Shared));

    public virtual (string?[] fields, bool eof) ReadNextRecord()
    {
        var bufferSize = 0;
        var index = 0;
        var eof = false;
        var isFirstCharOfRecord = true;
        var indexRecordSeparator = 0;
        var isFirstCharOfField = true;
        var fields = new List<string?>();
        var indexFieldStart = 0;
        var isCommentLine = false;
        var isFieldWithTextQualifier = false;
        var isEndingByTextQualifier = false;
        var isTextQualifierEscaped = false;
        Span<char> longField = stackalloc char[0];
        var longFieldIndex = 0;
        var isLastCharDelimiter = false;

        if (Buffer.Length == 0)
        {
            if (Reader.IsEof)
                bufferSize = 0;
            else
            {
                Buffer = Reader.Read();
                bufferSize = Buffer.Length;
            }

            eof = bufferSize == 0;
        }
        else
            bufferSize = Buffer.Length;

        var span = Buffer.Span;
        span = span.Slice(0, bufferSize);

        while (!eof && index < bufferSize)
        {
            if (index >= span.Length)
                Console.WriteLine(span.ToString());

            char c = span[index];
            if (c == '\0')
            {
                eof = true;
                break;
            }

            if (isFirstCharOfRecord)
            {
                isCommentLine = c == Profile.Descriptor.CommentChar;
                isFirstCharOfRecord = false;
            }

            if (isFirstCharOfField && !isCommentLine)
            {
                if (!Profile.Descriptor.SkipInitialSpace || c != ' ')
                {
                    isFirstCharOfField = false;
                    if (!Profile.ParserOptimizations.NoTextQualifier)
                    {
                        isFieldWithTextQualifier = c == Profile.Descriptor.QuoteChar;
                        isEndingByTextQualifier = false;
                        isTextQualifierEscaped = false;
                    }
                }
                else
                    indexFieldStart++;
            }
            else if (!Profile.ParserOptimizations.NoTextQualifier && c != Profile.Descriptor.Delimiter && c != Profile.Descriptor.LineTerminator[indexRecordSeparator] && !isFirstCharOfField)
            {
                isEndingByTextQualifier = c == Profile.Descriptor.QuoteChar && !isTextQualifierEscaped;
                isTextQualifierEscaped = c == Profile.Descriptor.EscapeChar && !isTextQualifierEscaped;
            }

            if (c == Profile.Descriptor.Delimiter && !isCommentLine && (isFieldWithTextQualifier == isEndingByTextQualifier))
            {
                if (longFieldIndex == 0)
                    fields.Add(FieldParser.ReadField(span, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                else
                {
                    fields.Add(FieldParser.ReadField(longField, longFieldIndex, span, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                    longField = Span<char>.Empty;
                    longFieldIndex = 0;
                }
                isFirstCharOfField = true;
                indexFieldStart = index + 1;
            }

            if (c == Profile.Descriptor.LineTerminator[indexRecordSeparator])
            {
                indexRecordSeparator++;
                if (indexRecordSeparator == Profile.Descriptor.LineTerminator.Length)
                {
                    if (!isCommentLine)
                    {
                        if (indexFieldStart <= index + longFieldIndex - Profile.Descriptor.LineTerminator.Length)
                        {
                            if (longFieldIndex == 0)
                                fields.Add(FieldParser.ReadField(span, indexFieldStart, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                            else
                            {
                                fields.Add(FieldParser.ReadField(longField, longFieldIndex, span, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                                longField = Span<char>.Empty;
                                longFieldIndex = 0;
                            }
                        }
                        Buffer = Buffer.Slice(index + 1, bufferSize - index - 1);
                        span = Buffer.Span;
                        return (fields.ToArray(), false);
                    }
                    else
                    {
                        bufferSize = bufferSize - index - 1;
                        Buffer = Buffer.Slice(index + 1);
                        span = Buffer.Span;
                        isCommentLine = false;
                        index = -1;
                        indexFieldStart = 0;
                    }
                    isFirstCharOfRecord = true;
                    isFirstCharOfField = true;
                    indexRecordSeparator = 0;
                    isFieldWithTextQualifier = false;
                    isEndingByTextQualifier = false;
                }
            }
            else
                indexRecordSeparator = 0;

            if (++index == bufferSize)
            {
                if (index == indexFieldStart)
                {
                    longField = longField.Slice(0, longFieldIndex);
                }
                else if (longField.Length >= longFieldIndex + index - indexFieldStart)
                {
                    span.Slice(indexFieldStart, index - indexFieldStart)
                        .CopyTo(longField.Slice(longFieldIndex));
                }
                else
                {
                    var newArray = Pool?.Rent(longFieldIndex + index - indexFieldStart) ?? new char[longFieldIndex + index - indexFieldStart];
                    var newSpan = newArray.AsSpan().Slice(0, longFieldIndex + index - indexFieldStart);
                    longField.CopyTo(newSpan);
                    var remaining = span.Slice(indexFieldStart, index - indexFieldStart);
                    remaining.CopyTo(newSpan.Slice(longFieldIndex));
                    longField = newSpan;
                    Pool?.Return(newArray);
                }

                longFieldIndex += index - indexFieldStart;
                indexFieldStart = 0;
                if (!Reader.IsEof)
                {
                    Buffer = Reader.Read();
                    bufferSize = Buffer.Length;
                    span = Buffer.Span;
                    eof = bufferSize == 0;
                }
                else
                {
                    bufferSize = 0;
                    eof = true;
                }
                index = 0;
                if (eof)
                    isLastCharDelimiter = true;
            }
        }

        if (eof && (index != indexFieldStart || longFieldIndex > 0 || isLastCharDelimiter) && !isCommentLine)
            if (longFieldIndex == 0)
                if (isLastCharDelimiter)
                    fields.Add(Profile.EmptyCell);
                else
                    fields.Add(FieldParser.ReadField(span, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
            else
                fields.Add(FieldParser.ReadField(longField, longFieldIndex, span, index, isFieldWithTextQualifier, isEndingByTextQualifier));

        return (fields.ToArray(), eof);
    }

    public int? CountRecords(StreamReader reader)
    {
        if (!Profile.ParserOptimizations.RowCountAtStart)
            return null;

        var count = CountRecordSeparators(reader);
        count -= Convert.ToInt16(Profile.Descriptor.Header);
        //RaiseProgressStatus($"{count} record{(count > 1 ? "s were" : " was")} identified.");

        reader.BaseStream.Position = 0;
        reader.DiscardBufferedData();
        return count;
    }

    public virtual int CountRecordSeparators(StreamReader reader)
    {
        int i = 0;
        int n = 0;
        int j = 0;
        bool separatorAtEnd = false;
        bool isCommentLine = false;
        bool isFirstCharOfLine = true;

        var array = Pool?.Rent(Profile.ParserOptimizations.BufferSize) ?? new char[Profile.ParserOptimizations.BufferSize];
        do
        {
            var buffer = new Span<char>(array);
            n = reader.ReadBlock(buffer);
            buffer = buffer.Slice(0, n);
            if (n > 0 && i == 0)
                i = 1;

            foreach (var c in buffer)
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
        } while (n > 0);

        if (separatorAtEnd)
            i -= 1;

        if (isCommentLine)
            i -= 1;

        if (array is not null)
            Pool?.Return(array);
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

    public virtual string[] ReadHeader()
    {
        var unnamedFieldIndex = 0;
        return ReadNextRecord().fields
                .Select(value => value is null || !Profile.Descriptor.Header
                                    ? $"field_{unnamedFieldIndex++}"
                                    : value).ToArray();
    }

    public void Dispose()
    {
        Reader.Dispose();
    }
}
