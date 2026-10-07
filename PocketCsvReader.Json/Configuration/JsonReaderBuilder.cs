using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonReaderBuilder : ReaderBuilder<JsonReaderBuilder>
{
    private string[]? _projectedProperties;

    public JsonReaderBuilder()
        : base(JsonProfile.DefaultParserOptimizations)
    { }

    public JsonReaderBuilder WithProjection(Func<JsonProjectionBuilder, JsonProjectionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _projectedProperties = configure(new JsonProjectionBuilder()).Build();
        return this;
    }

    public JsonReader Build()
        => new(new JsonProfile(
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations(),
            _projectedProperties));
}
