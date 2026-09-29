using PocketCsvReader.Configuration;

namespace PocketCsvReader.KeyValue.Configuration;

public sealed class KeyValueProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public KeyValueFormat Format { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;

    public KeyValueProfile(
        KeyValueFormat format,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        Format = format;
        Schema = schema;
        Resource = resource;
        Parsers = parsers;

        var requested = parserOptimizations ?? DefaultParserOptimizations;
        if (requested.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
        ParserOptimizations = requested with { ReadAhead = false, RowCountAtStart = false };
    }
}
