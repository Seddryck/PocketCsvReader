using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;
public class NdjsonReaderBuilder
{
    private NdjsonDialectDescriptorBuilder _dialectBuilder = new();
    private ISchemaDescriptorBuilder? _schemaBuilder;
    private ResourceDescriptorBuilder? _resourceBuilder;
    private RuntimeParsersDescriptorBuilder? _parserBuilder;

    public NdjsonReaderBuilder WithDialect(Func<NdjsonDialectDescriptorBuilder, NdjsonDialectDescriptorBuilder> func)
    {
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public NdjsonReaderBuilder WithDialect(NdjsonDialectDescriptorBuilder dialectBuilder)
    {
        _dialectBuilder = dialectBuilder;
        return this;
    }

    public NdjsonReaderBuilder WithSchema(Func<SchemaDescriptorBuilder, ISchemaDescriptorBuilder> func)
    {
        _schemaBuilder = func(new());
        return this;
    }

    public NdjsonReaderBuilder WithSchema(ISchemaDescriptorBuilder schemaBuilder)
    {
        _schemaBuilder = schemaBuilder;
        return this;
    }

    public NdjsonReaderBuilder WithResource(Func<ResourceDescriptorBuilder, ResourceDescriptorBuilder> func)
    {
        _resourceBuilder = func(new());
        return this;
    }

    public NdjsonReaderBuilder WithResource(ResourceDescriptorBuilder resourceBuilder)
    {
        _resourceBuilder = resourceBuilder;
        return this;
    }

    public NdjsonReaderBuilder WithParsers(Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> func)
    {
        _parserBuilder = func(new());
        return this;
    }

    public NdjsonReaderBuilder WithParsers(RuntimeParsersDescriptorBuilder parserBuilder)
    {
        _parserBuilder = parserBuilder;
        return this;
    }

    public NdjsonReader Build()
        => new (new NdjsonProfile(_dialectBuilder.Build(), _schemaBuilder?.Build(), _resourceBuilder?.Build(), _parserBuilder?.Build()));
}
