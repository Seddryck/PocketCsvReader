using PocketCsvReader.Configuration;

namespace PocketCsvReader.WebLogs;

public sealed class WebLogProfile : IProfile
{
    public WebLogFormat Format { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;

    public WebLogProfile(
        WebLogFormat format,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        var options = parserOptimizations ?? new ParserOptimizationOptions();
        if (options.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");

        var sequences = SequenceCollection.Concat(resource?.Sequences, ImmutableSequenceCollection.Empty);
        sequences.Add("-", null);

        Format = format;
        Schema = schema;
        Resource = (resource ?? new ResourceDescriptor()) with { Sequences = sequences.ToImmutable() };
        Parsers = parsers;
        ParserOptimizations = options with { RowCountAtStart = false };
    }
}
