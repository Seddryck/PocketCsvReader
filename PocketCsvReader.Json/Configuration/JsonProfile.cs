using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public bool TrackPosition => ParserOptimizations.TrackPosition;
    public ParserOptimizationOptions ParserOptimizations { get; }
    public IReadOnlyList<string>? ProjectedProperties { get; }
    public bool OrderedProjection { get; }
    /// <summary>Accept JSON comments and trailing commas. Strict JSON is the default.</summary>
    public bool Jsonc { get; }
    public string? RecordPath { get; }
    internal PocketCsvReader.Json.JsonRecordPath? ParsedRecordPath { get; }

    public JsonProfile(
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null,
        bool orderedProjection = false,
        string? recordPath = null)
        : this(false, schema, resource, parsers, parserOptimizations, projectedProperties, orderedProjection, recordPath)
    { }

    public JsonProfile(
        bool jsonc,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null,
        bool orderedProjection = false,
        string? recordPath = null)
    {
        Jsonc = jsonc;
        RecordPath = recordPath;
        ParsedRecordPath = recordPath is null ? null : PocketCsvReader.Json.JsonRecordPath.Parse(recordPath);
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
        ProjectedProperties = JsonProjection.Create(projectedProperties, orderedProjection);
        OrderedProjection = orderedProjection;
        if (ParserOptimizations.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
    }

    private static JsonProfile? _default;
    public static JsonProfile Default => _default ??= new JsonProfile();
}
