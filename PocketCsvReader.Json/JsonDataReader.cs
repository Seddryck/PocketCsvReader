using PocketCsvReader.FieldParsing;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

public class JsonDataReader : LabeledDataReader<JsonProfile>
{
    private JsonFieldBuffer? _decodingBuffer;
    public JsonDataReader(Stream stream, JsonProfile profile)
        : base(stream, profile, StringMapper.ForLabeledValues(profile.ParserOptimizations))
    { }

    protected override IRecordSource<JsonProfile> CreateRecordSource(StreamReader reader, JsonProfile profile)
        => new JsonRecordSource(reader, profile);

    protected override bool HasStableLabelShape => Profile.StableFieldLayout;

    protected override bool CacheInferredFieldParsers => Profile.StableFieldLayout;

    protected override ReadOnlyMemory<char> GetFieldMemory(ref FieldSpan field)
        => JsonValueDecoder.GetMemory(CurrentRecordMemory, ref field, ref _decodingBuffer);

    protected override string GetStringValue(int i)
    {
        var isBuiltIn = GetType() == typeof(JsonDataReader);
        var hints = StringMapper.HasLastValueHints;
        if (isBuiltIn && hints
            && JsonValueDecoder.TryMapString(CurrentRecordMemory, CurrentFieldSpans, i, StringMapper, out var pooled))
            return pooled;

        var value = GetValueOrThrow(i);
        if (isBuiltIn && Profile.ParserOptimizations.PoolString is null
            && CurrentFieldSpans![i].DecodedValue is { } decoded)
            return decoded;

        return hints ? StringMapper.Map(i, value) : StringMapper.Map(value);
    }

    protected override bool IsNull(int i)
    {
        if (GetType() != typeof(JsonDataReader)) return base.IsNull(i);
        var fields = CurrentFieldSpans ?? throw new InvalidOperationException("Current record is not set.");
        if ((uint)i >= (uint)fields.Length) throw new ArgumentOutOfRangeException(nameof(i));
        return fields[i].Value.IsNull;
    }
}
