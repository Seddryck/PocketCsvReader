using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;
public class RecordParser
{
    protected internal CsvProfile Profile { get; private set; }
    protected FieldParser FieldParser { get; private set; }

    public RecordParser(CsvProfile profile)
        => (Profile, FieldParser) = (profile, new(profile));

    public virtual (string?[] fields, bool eof) ReadNextRecord(ref Memory<char> buffer)
    {
        return ReadNextRecord(null, ref buffer);
    }

    public virtual (string?[] fields, bool eof) ReadNextRecord(StreamReader? reader, ref Memory<char> buffer)
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

        if (buffer.Length > 0 && buffer.Span[0] != '\0')
        {
            bufferSize = buffer.Length;
        }
        else
        {
            buffer = new Memory<char>(new char[Profile.BufferSize]);
            bufferSize = reader?.ReadBlock(buffer.Span) ?? throw new ArgumentNullException(nameof(reader));
            eof = bufferSize == 0;
        }

        while (!eof && index < bufferSize)
        {
            char c = buffer.Span[index];
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
                isFirstCharOfField = false;
                if (!Profile.ParserOptimizations.NoTextQualifier)
                {
                    isFieldWithTextQualifier = c == Profile.Descriptor.QuoteChar;
                    isEndingByTextQualifier = false;
                    isTextQualifierEscaped = false;
                }
            }
            else if (!Profile.ParserOptimizations.NoTextQualifier && c != Profile.Descriptor.Delimiter && c != Profile.Descriptor.LineTerminator[indexRecordSeparator] && !isFirstCharOfField)
            {
                isEndingByTextQualifier = c == Profile.Descriptor.QuoteChar && !isTextQualifierEscaped;
                isTextQualifierEscaped = c == Profile.Descriptor.EscapeChar && !isTextQualifierEscaped;
            }

            if (c == Profile.Descriptor.Delimiter && !isCommentLine && (isFieldWithTextQualifier == isEndingByTextQualifier))
            {
                if (longFieldIndex == 0)
                    fields.Add(FieldParser.ReadField(buffer.Span, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                else
                {
                    fields.Add(FieldParser.ReadField(longField, longFieldIndex, buffer.Span, index, isFieldWithTextQualifier, isEndingByTextQualifier));
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
                                fields.Add(FieldParser.ReadField(buffer.Span, indexFieldStart, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                            else
                            {
                                fields.Add(FieldParser.ReadField(longField, longFieldIndex, buffer.Span, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                                longField = Span<char>.Empty;
                                longFieldIndex = 0;
                            }
                        }
                        buffer = buffer.Slice(index + 1, bufferSize - index - 1);
                        return (fields.ToArray(), false);
                    }
                    else
                    {
                        bufferSize = bufferSize - index - 1;
                        buffer = buffer.Slice(index + 1);
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
                if (longField.Length >= longFieldIndex + index - indexFieldStart)
                {
                    buffer.Span.Slice(indexFieldStart, index - indexFieldStart).CopyTo(longField.Slice(longFieldIndex));
                }
                else
                {
                    var newArray = new Span<char>(new char[longFieldIndex + index - indexFieldStart]);
                    longField.CopyTo(newArray);
                    var remaining = buffer.Slice(indexFieldStart, index - indexFieldStart);
                    remaining.Span.CopyTo(newArray.Slice(longFieldIndex));
                    longField = newArray;
                }

                longFieldIndex += index - indexFieldStart;
                indexFieldStart = 0;
                buffer = new Memory<char>(new char[Profile.BufferSize]);
                bufferSize = reader?.ReadBlock(buffer.Span) ?? throw new ArgumentNullException(nameof(reader));
                eof = bufferSize == 0;
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
                    fields.Add(FieldParser.ReadField(buffer.Span, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
            else
                fields.Add(FieldParser.ReadField(longField, longFieldIndex, buffer.Span, index, isFieldWithTextQualifier, isEndingByTextQualifier));

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

    private int BufferSize = 4096;
    public virtual int CountRecordSeparators(StreamReader reader)
    {
        int i = 0;
        int n = 0;
        int j = 0;
        bool separatorAtEnd = false;
        bool isCommentLine = false;
        bool isFirstCharOfLine = true;

        do
        {
            char[] buffer = new char[BufferSize];
            n = reader.Read(buffer, 0, BufferSize);
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

        return i;
    }

    public string GetFirstRecord(StreamReader reader, string recordSeparator, int bufferSize)
    {
        var stringBuilder = new StringBuilder();
        int j = 0;

        while (true)
        {
            char[] buffer = new char[bufferSize];
            reader.Read(buffer, 0, bufferSize);

            foreach (var c in buffer)
            {
                if (c != '\0')
                {
                    stringBuilder.Append(c);
                    if (c == recordSeparator[j])
                    {
                        j++;
                        if (j == recordSeparator.Length)
                            return stringBuilder.ToString();
                    }
                    else
                        j = 0;
                }
                else
                    return stringBuilder.ToString();
            }
        }
    }

    public virtual string[] ReadHeader(StreamReader? reader, ref Memory<char> buffer)
    {
        var unnamedFieldIndex = 0;
        return ReadNextRecord(reader, ref buffer).fields
                .Select(value => value is null || !Profile.Descriptor.Header
                                    ? $"field_{unnamedFieldIndex++}"
                                    : value).ToArray();
    }
}
