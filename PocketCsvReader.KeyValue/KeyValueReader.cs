using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

public sealed class KeyValueReader : FlatFileReader<KeyValueProfile, KeyValueDataReader>
{
    public KeyValueFormat Format => Profile.Format;

    public KeyValueReader(KeyValueProfile profile)
        : base(profile)
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    internal override IEnumerable<string?>? SeekConfiguration => Profile.StableFieldLayout ? null :
        ["key-value", Profile.Format.ToString(), Profile.Projection is null ? null : "projection",
            .. Profile.Projection?.SelectMany(column => new[] { column.Key, column.Name }) ?? []];

    protected override KeyValueDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);
}
