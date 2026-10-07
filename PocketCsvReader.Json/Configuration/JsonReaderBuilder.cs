using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonReaderBuilder : ReaderBuilder<JsonReaderBuilder>
{
    private string[]? _projectedProperties;
    private bool _orderedProjection;

    public JsonReaderBuilder()
        : base(JsonProfile.DefaultParserOptimizations)
    { }

    public JsonReaderBuilder WithProjection(Func<JsonProjectionBuilder, JsonProjectionBuilder> configure)
        => ConfigureProjection(configure, ordered: false);

    /// <summary>
    /// Projects properties in the order in which they must occur in each JSON object.
    /// Non-projected properties may occur between projected properties.
    /// </summary>
    public JsonReaderBuilder WithOrderedProjection(Func<JsonProjectionBuilder, JsonProjectionBuilder> configure)
        => ConfigureProjection(configure, ordered: true);

    private JsonReaderBuilder ConfigureProjection(
        Func<JsonProjectionBuilder, JsonProjectionBuilder> configure,
        bool ordered)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _projectedProperties = configure(new JsonProjectionBuilder()).Build();
        _orderedProjection = ordered;
        return this;
    }

    public JsonReader Build()
        => new(new JsonProfile(
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations(),
            _projectedProperties,
            _orderedProjection));
}
