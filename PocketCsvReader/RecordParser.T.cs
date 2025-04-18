using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PocketCsvReader;
public class RecordParser<T> : RecordParser
{
    protected SpanMapper<T> SpanMapper { get; }

    public RecordParser(StreamReader reader, CsvProfile profile, SpanMapper<T> spanMapper)
        : this(reader, profile, spanMapper, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, CsvProfile profile, SpanMapper<T> spanMapper, ArrayPool<char>? pool)
        : this(profile, profile.ParserOptimizations.ReadAhead
                    ? new DoubleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
                    : new SingleBuffer(reader, profile.ParserOptimizations.BufferSize, pool)
              , spanMapper, pool
              )
    { }

    protected RecordParser(CsvProfile profile, IBufferReader buffer, SpanMapper<T> spanMapper, ArrayPool<char>? pool)
        : base(profile, buffer, pool)
    {
        SpanMapper = spanMapper;
    }

    /// <summary>
    /// Determines if the end of the CSV file has been reached and maps the next record to an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <param name="value">The mapped record of type <typeparamref name="T"/>, or the default value if no fields are present.</param>
    /// <returns><c>true</c> if the end of the file has been reached; otherwise, <c>false</c>.</returns>
    public virtual bool IsEndOfFile(out T value)
    {
        var eof = IsEndOfFile(out RecordSpan rawRecord, out RecordState state);
        value = (rawRecord.FieldSpans?.Length ?? 0) == 0 ? default! : SpanMapper(rawRecord.Span, rawRecord.FieldSpans!);
        return eof;
    }
}
