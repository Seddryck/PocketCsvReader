using PocketCsvReader.Configuration;

namespace PocketCsvReader.Json.Configuration;

public class JsonProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public IReadOnlyList<string>? ProjectedProperties { get; }

    public JsonProfile(
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null)
    {
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
        var projection = projectedProperties?.ToArray();
        if (projection is { Length: 0 })
            throw new ArgumentException("At least one projected property must be configured.", nameof(projectedProperties));
        if (projection?.Any(string.IsNullOrEmpty) == true)
            throw new ArgumentException("Projected property names cannot be null or empty.", nameof(projectedProperties));
        if (projection is not null
            && projection.Distinct(StringComparer.Ordinal).Count() != projection.Length)
        {
            throw new ArgumentException("Projected properties must be unique.", nameof(projectedProperties));
        }
        ProjectedProperties = projection is null ? null : Array.AsReadOnly(projection);
        if (ParserOptimizations.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
    }

    private static JsonProfile? _default;
    public static JsonProfile Default => _default ??= new JsonProfile();
}
