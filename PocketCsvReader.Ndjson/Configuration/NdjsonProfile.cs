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

    public NdjsonProfile(
        NdjsonDialectDescriptor dialect,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null,
        IEnumerable<string>? projectedProperties = null)
    {
        Dialect = dialect;
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
