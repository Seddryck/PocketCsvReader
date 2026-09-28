using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class RecordParser : BaseRecordParser<NdjsonProfile>
{
    private readonly StreamReader _reader;

    public RecordParser(StreamReader reader, NdjsonProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, NdjsonProfile profile, ArrayPool<char>? pool)
        : base(profile, new SingleBuffer(reader, 64*1024, pool), pool, (p) => new NdjsonParser(p.Dialect))
        => _reader = reader;

    protected RecordParser(NdjsonProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        : base(profile, buffer, pool, (p) => new NdjsonParser(p.Dialect))
        => _reader = null!;

    public override bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (_reader is null)
            return base.IsEndOfFile(out record, out recordState);

        string? line;
        do
        {
            line = _reader.ReadLine();
        } while (line is not null && string.IsNullOrWhiteSpace(line));

        if (line is null)
        {
            record = new RecordSpan([], []);
            recordState = RecordState.Eof;
            return true;
        }

        var fields = new JsonRecordParser(line).ParseRoot();
        record = new RecordSpan(line.AsSpan(), fields);
        recordState = RecordState.Record;
        return _reader.Peek() < 0;
    }
}
