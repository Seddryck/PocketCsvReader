using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;

public class NdjsonProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public NdjsonDialectDescriptor Dialect { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public ParserOptimizationOptions ParserOptimizations { get; }
    public IReadOnlyList<string>? ProjectedProperties { get; }
    public bool OrderedProjection { get; }
    /// <summary>
    /// Gets whether the input is trusted to contain the same recursive JSON structure and property order.
    /// The first object establishes object property counts, array lengths, labels, and ordinals at every level;
    /// later names are consumed as JSON syntax without being decoded or compared. Structural drift is rejected,
    /// while scalar leaf types may still vary.
    /// </summary>
    public bool StableObjectShape { get; }

    public NdjsonProfile(
        NdjsonDialectDescriptor dialect,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null,
        bool orderedProjection = false,
        bool stableObjectShape = false)
    {
        Dialect = dialect;
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
        var projection = projectedProperties?.ToArray();
        if (projection is { Length: 0 })
            throw new ArgumentException("At least one projected property must be configured.", nameof(projectedProperties));
        if (orderedProjection && projection is null)
            throw new ArgumentException("An ordered projection requires projected properties.", nameof(orderedProjection));
        if (projection?.Any(string.IsNullOrEmpty) == true)
            throw new ArgumentException("Projected property names cannot be null or empty.", nameof(projectedProperties));
        if (projection is not null
            && projection.Distinct(StringComparer.Ordinal).Count() != projection.Length)
        {
            throw new ArgumentException("Projected properties must be unique.", nameof(projectedProperties));
        }
        ProjectedProperties = projection is null ? null : Array.AsReadOnly(projection);
        OrderedProjection = orderedProjection;
        StableObjectShape = stableObjectShape;
        if (ParserOptimizations.BufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(parserOptimizations), "The parser buffer size must be positive.");
    }

    public NdjsonProfile(string recordSeparator)
    {
        Dialect = new NdjsonDialectDescriptorBuilder()
            .WithLineTerminator(recordSeparator)
            .Build();
        ParserOptimizations = DefaultParserOptimizations;
    }

    private static NdjsonProfile? _default;
    public static NdjsonProfile Default
    {
        get => _default ??= new NdjsonProfile(new NdjsonDialectDescriptor());
    }
}
