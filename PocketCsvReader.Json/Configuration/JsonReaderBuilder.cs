using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonReaderBuilder : ReaderBuilder<JsonReaderBuilder>
{
    public JsonReaderBuilder()
        : base(JsonProfile.DefaultParserOptimizations)
    { }

    public JsonReader Build()
        => new(new JsonProfile(
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
