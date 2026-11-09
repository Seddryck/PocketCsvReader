using PocketCsvReader.Configuration;

namespace PocketCsvReader.KeyValue.Configuration;

public sealed class KeyValueProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public IReadOnlyList<ReaderFilter> Filters { get; init; } = Array.Empty<ReaderFilter>();
    public int Skip { get; init; }
    public int? Take { get; init; }
    /// <summary>Trusts that records contain the first record's keys in the same order.</summary>
    /// <value>True to reuse the first record's layout. Otherwise, false.</value>
    public bool StableFieldLayout { get; init; }
    internal ProjectedColumn[]? Projection { get; init; }
    public KeyValueFormat Format { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public bool TrackPosition => ParserOptimizations.TrackPosition;
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
