using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private ISchemaDescriptorBuilder? _schemaBuilder;
    private ResourceDescriptorBuilder? _resourceBuilder;
    private RuntimeParsersDescriptorBuilder? _parserBuilder;

    public CsvReaderBuilder WithDialect(Func<DialectDescriptorBuilder, DialectDescriptorBuilder> func)
    {
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public CsvReaderBuilder WithDialect(DialectDescriptorBuilder dialectBuilder)
    {
        _dialectBuilder = dialectBuilder;
        return this;
    }

    public CsvReaderBuilder WithSchema(Func<SchemaDescriptorBuilder, ISchemaDescriptorBuilder> func)
    {
        _schemaBuilder = func(new());
        return this;
    }

    public CsvReaderBuilder WithSchema(ISchemaDescriptorBuilder schemaBuilder)
    {
        _schemaBuilder = schemaBuilder;
        return this;
    }

    public CsvReaderBuilder WithResource(Func<ResourceDescriptorBuilder, ResourceDescriptorBuilder> func)
    {
        _resourceBuilder = func(new());
        return this;
    }

    public CsvReaderBuilder WithResource(ResourceDescriptorBuilder resourceBuilder)
    {
        _resourceBuilder = resourceBuilder;
        return this;
    }

    public CsvReaderBuilder WithParsers(Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> func)
    {
        _parserBuilder = func(new());
        return this;
    }

    public CsvReaderBuilder WithParsers(RuntimeParsersDescriptorBuilder parserBuilder)
    {
        _parserBuilder = parserBuilder;
        return this;
    }

    public CsvReader Build()
        => new (new CsvProfile(_dialectBuilder.Build(), _schemaBuilder?.Build(), _resourceBuilder?.Build(), _parserBuilder?.Build()));
}
