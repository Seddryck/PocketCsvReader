using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;

public class NdjsonProfile : IProfile
{
    internal static ParserOptimizationOptions DefaultParserOptimizations { get; }
        = new(BufferSize: 64 * 1024, ReadAhead: false, RowCountAtStart: false);

    public NdjsonDialectDescriptor Dialect { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }
    public bool TrackPosition => ParserOptimizations.TrackPosition;
    public ParserOptimizationOptions ParserOptimizations { get; }
    public IReadOnlyList<string>? ProjectedProperties { get; }
    public bool OrderedProjection { get; }
    public string? RecordPath { get; }
    internal PocketCsvReader.Json.JsonRecordPath? ParsedRecordPath { get; }
    /// <summary>
    /// Gets whether the input is trusted to contain the same recursive JSON structure and property order.
    /// The first object establishes object property counts, array lengths, labels, and ordinals at every level;
    /// later names are consumed as JSON syntax without being decoded or compared. Structural drift is rejected,
    /// while scalar leaf types may still vary.
    /// </summary>
    /// <value>True to reuse the first object structure; otherwise false.</value>
    public bool StableObjectShape { get; }

    public NdjsonProfile(
        NdjsonDialectDescriptor dialect,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null,
        bool orderedProjection = false,
        bool stableObjectShape = false,
        string? recordPath = null)
    {
        Dialect = dialect;
        RecordPath = recordPath;
        ParsedRecordPath = recordPath is null ? null : PocketCsvReader.Json.JsonRecordPath.Parse(recordPath);
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
        ProjectedProperties = JsonProjection.Create(projectedProperties, orderedProjection);
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
