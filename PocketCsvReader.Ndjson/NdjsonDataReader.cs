using PocketCsvReader.FieldParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

public class NdjsonDataReader : LabeledDataReader<NdjsonProfile>
{
    public NdjsonDataReader(Stream stream, NdjsonProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    protected override IRecordSource<NdjsonProfile> CreateRecordSource(StreamReader reader, NdjsonProfile profile)
        => new NdjsonRecordSource(reader, profile);
}
