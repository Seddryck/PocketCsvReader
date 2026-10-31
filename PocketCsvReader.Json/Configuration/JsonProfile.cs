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
    /// <summary>Accept line and block comments. Disabled by default.</summary>
    public bool AllowComments { get; }
    /// <summary>Accept a single trailing comma in objects and arrays. Disabled by default.</summary>
    public bool AllowTrailingCommas { get; }
    public string? RecordPath { get; }
    internal PocketCsvReader.Json.JsonRecordPath? ParsedRecordPath { get; }

    // Retain the original constructor signature for existing compiled consumers.
    public JsonProfile(
        SchemaDescriptor? schema,
        ResourceDescriptor? resource,
        RuntimeParsersDescriptor? parsers,
        ParserOptimizationOptions? parserOptimizations,
        IEnumerable<string>? projectedProperties,
        bool orderedProjection,
        string? recordPath)
        : this(schema, resource, parsers, parserOptimizations, projectedProperties, orderedProjection, recordPath, false, false)
    { }

    public JsonProfile(
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null,
        bool orderedProjection = false,
        string? recordPath = null,
        bool allowComments = false,
        bool allowTrailingCommas = false)
    {
        AllowComments = allowComments;
        AllowTrailingCommas = allowTrailingCommas;
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
