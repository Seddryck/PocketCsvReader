using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder : ReaderBuilder<CsvReaderBuilder>
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private RowNumberOptions? _rowNumber;

    /// <summary>Prepends a generated integer column, numbered from one across the returned records.</summary>
    public CsvReaderBuilder WithRowNumber(string columnName = "rowNumber")
    {
        var options = new RowNumberOptions(columnName);
        options.Validate();
        _rowNumber = options;
        return this;
    }

    /// <summary>Disables the generated row-number column.</summary>
    public CsvReaderBuilder WithoutRowNumber()
    {
        _rowNumber = null;
        return this;
    }
    private StreamInitializationOptions _streamInitialization = StreamInitializationOptions.Default;

    public CsvReaderBuilder WithStreamInitialization(StreamInitializationOptions options)
    {
        _streamInitialization = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
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
            ParserOptimizations = BuildParserOptimizations(),
            StreamInitialization = _streamInitialization,
            RowNumber = _rowNumber
        };
        return new CsvReader(profile);
    }
}
