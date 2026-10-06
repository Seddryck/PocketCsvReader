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

    public JsonProfile(
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
        if (ParserOptimizations.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
    }

    private static JsonProfile? _default;
    public static JsonProfile Default => _default ??= new JsonProfile();
}
