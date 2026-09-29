using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

public sealed class KeyValueReader : FlatFileReader<KeyValueProfile, KeyValueDataReader>
{
    public KeyValueFormat Format => Profile.Format;

    public KeyValueReader(KeyValueProfile profile)
        : base(profile)
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override KeyValueDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);
}
