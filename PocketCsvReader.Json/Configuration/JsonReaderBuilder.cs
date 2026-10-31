using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonReaderBuilder : ReaderBuilder<JsonReaderBuilder>
{
    private string[]? _projectedProperties;
    private bool _orderedProjection;
    private string? _recordPath;
    private bool _jsonc;

    /// <summary>Enables line/block comments and trailing commas.</summary>
    public JsonReaderBuilder WithJsonc()
    {
        _jsonc = true;
        return this;
    }

    /// <summary>Selects the nodes exposed as rows using the supported JSONPath subset.</summary>
    public JsonReaderBuilder WithRecordPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _recordPath = path;
        return this;
    }

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
            _jsonc,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations(),
            _projectedProperties,
            _orderedProjection,
            _recordPath));
}
