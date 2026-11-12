using PocketCsvReader.Configuration;

namespace PocketCsvReader.Markdown;

public sealed class MarkdownProfile : IProfile
{
    public int SkipTables { get; }
    public string? TextMarker { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public bool TrackPosition => ParserOptimizations.TrackPosition;
    public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;
    public IReadOnlyList<ReaderFilter> Filters { get; init; } = Array.Empty<ReaderFilter>();
    public int Skip { get; init; }
    public int? Take { get; init; }
    public BadDataPolicy? BadDataPolicy { get; init; }

    public MarkdownProfile(
        int skipTables = 0,
        string? textMarker = null,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skipTables);
        if (textMarker is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(textMarker);
            if (textMarker.Contains('\r') || textMarker.Contains('\n'))
                throw new ArgumentException("The text marker must fit on one line.", nameof(textMarker));
            if (skipTables != 0)
                throw new ArgumentException("Choose table skipping or text-marker selection.", nameof(skipTables));
        }

        var options = parserOptimizations ?? new ParserOptimizationOptions();
        if (options.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");

        SkipTables = skipTables;
        TextMarker = textMarker?.Trim();
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = options with { RowCountAtStart = false };
    }
}
