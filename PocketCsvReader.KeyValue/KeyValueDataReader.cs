using PocketCsvReader.FieldParsing;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

public sealed class KeyValueDataReader : LabeledDataReader<KeyValueProfile>
{
    public KeyValueDataReader(Stream stream, KeyValueProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    protected override IRecordSource<KeyValueProfile> CreateRecordSource(StreamReader reader, KeyValueProfile profile)
        => new KeyValueRecordSource(reader, profile);
}
