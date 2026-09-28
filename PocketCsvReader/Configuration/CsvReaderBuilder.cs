using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder : ReaderBuilder<CsvReaderBuilder>
{
    private DialectDescriptorBuilder _dialectBuilder = new();

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

    public CsvReader Build()
    {
        var profile = new CsvProfile(
            _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers())
        {
            ParserOptimizations = BuildParserOptimizations()
        };
        return new CsvReader(profile);
    }
}
