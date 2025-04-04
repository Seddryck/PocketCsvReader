using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;

public class NdjsonProfile : IProfile
{
    public DialectDescriptor Dialect { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }

    public NdjsonProfile(DialectDescriptor dialect, SchemaDescriptor? schema = null)
        => (Dialect, Schema) = (dialect, schema);

    public NdjsonProfile(string recordSeparator)
    {
        Dialect = new DialectDescriptorBuilder()
            .WithLineTerminator(recordSeparator)
            .Build();
    }

    private static NdjsonProfile? _default;
    public static NdjsonProfile Default
    {
        get => _default ??= new NdjsonProfile(new DialectDescriptor());
    }
}
