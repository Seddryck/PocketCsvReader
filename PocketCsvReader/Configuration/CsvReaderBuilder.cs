using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private readonly ProfileBuilderComponents _components = new();

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
        _components.ConfigureSchema(func);
        return this;
    }

    public CsvReaderBuilder WithSchema(ISchemaDescriptorBuilder schemaBuilder)
    {
        _components.ConfigureSchema(schemaBuilder);
        return this;
    }

    public CsvReaderBuilder WithResource(Func<ResourceDescriptorBuilder, ResourceDescriptorBuilder> func)
    {
        _components.ConfigureResource(func);
        return this;
    }

    public CsvReaderBuilder WithResource(ResourceDescriptorBuilder resourceBuilder)
    {
        _components.ConfigureResource(resourceBuilder);
        return this;
    }

    public CsvReaderBuilder WithParsers(Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> func)
    {
        _components.ConfigureParsers(func);
        return this;
    }

    public CsvReaderBuilder WithParsers(RuntimeParsersDescriptorBuilder parserBuilder)
    {
        _components.ConfigureParsers(parserBuilder);
        return this;
    }

    public CsvReaderBuilder WithParserOptimizations(ParserOptimizationOptions options)
    {
        _components.ConfigureParserOptimizations(options);
        return this;
    }

    public CsvReader Build()
    {
        var profile = new CsvProfile(
            _dialectBuilder.Build(),
            _components.BuildSchema(),
            _components.BuildResource(),
            _components.BuildParsers())
        {
            ParserOptimizations = _components.ParserOptimizations
        };
        return new CsvReader(profile);
    }
}
