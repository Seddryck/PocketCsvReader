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

    public NdjsonProfile(
        NdjsonDialectDescriptor dialect,
        SchemaDescriptor? schema = null,
        ResourceDescriptor? resource = null,
        RuntimeParsersDescriptor? parsers = null,
        ParserOptimizationOptions? parserOptimizations = null)
    {
        Dialect = dialect;
        Schema = schema;
        Resource = resource;
        Parsers = parsers;
        ParserOptimizations = parserOptimizations ?? DefaultParserOptimizations;
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
