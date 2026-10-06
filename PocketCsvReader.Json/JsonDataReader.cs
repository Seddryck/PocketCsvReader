using PocketCsvReader.FieldParsing;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json;

public class JsonDataReader : LabeledDataReader<JsonProfile>
{
    public JsonDataReader(Stream stream, JsonProfile profile)
        : base(stream, profile, new StringMapper(profile.ParserOptimizations.PoolString))
    { }

    protected override IRecordSource<JsonProfile> CreateRecordSource(StreamReader reader, JsonProfile profile)
        => new JsonRecordSource(reader, profile);
}
