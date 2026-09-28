using System.Buffers;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

public class RecordParser : BaseRecordParser<NdjsonProfile>
{
    private readonly NdjsonRecordSource? _recordSource;

    public RecordParser(StreamReader reader, NdjsonProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, NdjsonProfile profile, ArrayPool<char>? pool)
        : base(
            profile,
            new SingleBuffer(reader, profile.ParserOptimizations.BufferSize, pool),
            pool,
            p => new NdjsonParser(p.Dialect))
        => _recordSource = new NdjsonRecordSource(reader, profile);

    protected RecordParser(NdjsonProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        : base(profile, buffer, pool, p => new NdjsonParser(p.Dialect))
    { }

    public override bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
        => _recordSource is null
            ? base.IsEndOfFile(out record, out recordState)
            : _recordSource.IsEndOfFile(out record, out recordState);
}
