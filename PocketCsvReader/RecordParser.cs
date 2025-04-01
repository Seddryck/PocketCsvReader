using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PocketCsvReader;
public class RecordParser : BaseRecordParser<CsvProfile>
{
    public RecordParser(StreamReader reader, CsvProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, CsvProfile profile, ArrayPool<char>? pool)
        : base(profile, profile.ParserOptimizations.ReadAhead
                    ? new DoubleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
                    : new SingleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
              , pool)
    { }

    protected RecordParser(CsvProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        : base(profile, buffer, pool)
    { }

    protected override CharParser CreateCharParser(CsvProfile profile)
        => new(profile);
    

    public virtual string[][] ReadHeaders()
    {
        if (!Profile.Dialect.Header)
            return [];

        var headerMapper = new SpanMapper<string[]>((span, fieldSpans) =>
        {
            var headers = new string[fieldSpans.Count()];
            var index = 0;
            foreach (var fieldSpan in fieldSpans)
                headers[index++] = span.Slice(fieldSpan.ValueStart, fieldSpan.ValueLength).ToString();
            return headers;
        });

        var headerList = new List<string[]>();
        var rowCount = 1;
        while (rowCount <= Profile.Dialect.HeaderRows.Max())
        {
            ReadNextRecord(out RecordSpan rawRecord);
            if (Profile.Dialect.HeaderRows.Contains(rowCount))
            {
                var fields = rawRecord.FieldSpans.Length == 0 ? [] : headerMapper(rawRecord.Span, rawRecord.FieldSpans);
                headerList.Add(fields);
            }
            rowCount++;
        }
        return [.. headerList];
    }

    public int? CountRecords()
    {
        if (!Profile.ParserOptimizations.RowCountAtStart)
            return null;

        var count = CountRecordSeparators();
        count -= Convert.ToInt16(Profile.Dialect.Header);

        CharParser.Reset();
        Reader.Reset();
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
                index -= Profile.Dialect.LineTerminator.Length - 1;
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
}
