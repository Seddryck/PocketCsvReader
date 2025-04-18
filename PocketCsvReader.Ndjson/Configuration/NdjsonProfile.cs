using System;
using System.Collections.Immutable;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;

public class NdjsonProfile : IProfile
{
    public NdjsonDialectDescriptor Dialect { get; }
    public SchemaDescriptor? Schema { get; }
    public ResourceDescriptor? Resource { get; }
    public RuntimeParsersDescriptor? Parsers { get; }

    public NdjsonProfile(NdjsonDialectDescriptor dialect, SchemaDescriptor? schema = null, ResourceDescriptor? resource = null, RuntimeParsersDescriptor? parsers= null)
        => (Dialect, Schema, Resource, Parsers) = (dialect, schema, resource, parsers);

    public NdjsonProfile(string recordSeparator)
    {
        Dialect = new NdjsonDialectDescriptorBuilder()
            .WithLineTerminator(recordSeparator)
            .Build();
    }

    private static NdjsonProfile? _default;
    public static NdjsonProfile Default
    {
        get => _default ??= new NdjsonProfile(new NdjsonDialectDescriptor());
    }
}
