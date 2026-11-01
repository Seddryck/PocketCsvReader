using PocketCsvReader.FieldParsing;
using PocketCsvReader.Json;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

public class NdjsonDataReader : LabeledDataReader<NdjsonProfile>
{
    private JsonFieldBuffer? _decodingBuffer;
    public NdjsonDataReader(Stream stream, NdjsonProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    protected override IRecordSource<NdjsonProfile> CreateRecordSource(StreamReader reader, NdjsonProfile profile)
        => new NdjsonRecordSource(reader, profile);

    protected override bool HasStableLabelShape => Profile.StableObjectShape;

    protected override bool CacheInferredFieldParsers => Profile.StableObjectShape;

    protected override ReadOnlyMemory<char> GetFieldMemory(ref FieldSpan field)
        => JsonValueDecoder.GetMemory(CurrentRecordMemory, ref field, ref _decodingBuffer);

    protected override string GetStringValue(int i)
    {
        var value = GetValueOrThrow(i);
        return GetType() == typeof(NdjsonDataReader) && Profile.ParserOptimizations.PoolString is null && CurrentFieldSpans![i].DecodedValue is { } decoded
            ? decoded : StringMapper.Map(value)!;
    }

    protected override bool IsNull(int i)
    {
        if (GetType() != typeof(NdjsonDataReader)) return base.IsNull(i);
        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        if ((uint)i >= (uint)fields.Length) throw new ArgumentOutOfRangeException(nameof(i));
        return fields[i].Value.IsNull;
    }
}
