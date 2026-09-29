using PocketCsvReader.Configuration;

namespace PocketCsvReader.KeyValue.Configuration;

public abstract class KeyValueReaderBuilder<TBuilder> : ReaderBuilder<TBuilder>
    where TBuilder : KeyValueReaderBuilder<TBuilder>
{
    private readonly KeyValueFormat _format;

    protected KeyValueReaderBuilder(KeyValueFormat format)
        : base(KeyValueProfile.DefaultParserOptimizations)
        => _format = format;

    protected sealed override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { ReadAhead = false, RowCountAtStart = false };

    protected KeyValueReader BuildReader()
        => new(new KeyValueProfile(
            _format,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()));
}
