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

    public CsvReader Build()
    {
        var csvReader = new CsvReader(new CsvProfile(_dialectBuilder.Build(), _schemaBuilder?.Build()));
        return csvReader;
    }

}
