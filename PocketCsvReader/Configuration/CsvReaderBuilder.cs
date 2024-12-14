using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder
{
    private DialectDescriptorBuilder _descriptorBuilder = new();

    public CsvReaderBuilder WithDialectDescriptor(Func<DialectDescriptorBuilder, DialectDescriptorBuilder> func)
    {
        _descriptorBuilder = func(_descriptorBuilder);
        return this;
    }

    public CsvReader Build()
    {
        var csvReader = new CsvReader(new CsvProfile(_descriptorBuilder.Build()));
        return csvReader;
    }

}
