using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private SchemaDescriptorBuilder _schemaBuilder = new();

    public CsvReaderBuilder WithDialectDescriptor(Func<DialectDescriptorBuilder, DialectDescriptorBuilder> func)
    {
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public CsvReaderBuilder WithDialectDescriptor(DialectDescriptorBuilder dialectBuilder)
    {
        _dialectBuilder = dialectBuilder;
        return this;
    }

    public CsvReaderBuilder WithSchemaDescriptor(Func<SchemaDescriptorBuilder, SchemaDescriptorBuilder> func)
    {
        _schemaBuilder = func(_schemaBuilder);
        return this;
    }

    public CsvReaderBuilder WithSchemaDescriptor(SchemaDescriptorBuilder schemaBuilder)
    {
        _schemaBuilder = schemaBuilder;
        return this;
    }

    public CsvReader Build()
    {
        var csvReader = new CsvReader(new CsvProfile(_dialectBuilder.Build(), _schemaBuilder.Build()));
        return csvReader;
    }

}
