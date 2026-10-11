using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder : ReaderBuilder<CsvReaderBuilder>
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private DialectDescriptor? _dialect;

    public CsvReaderBuilder WithDialect(Func<DialectDescriptorBuilder, DialectDescriptorBuilder> func)
    {
        _dialect = null;
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public CsvReaderBuilder WithDialect(DialectDescriptorBuilder dialectBuilder)
    {
        _dialect = null;
        _dialectBuilder = dialectBuilder;
        return this;
    }

    public CsvReaderBuilder WithDialect(DialectDescriptor dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        return this;
    }

    public CsvReader Build()
    {
        var profile = new CsvProfile(
            _dialect ?? _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers())
        {
            ParserOptimizations = BuildParserOptimizations()
        };
        return new CsvReader(profile);
    }
}
