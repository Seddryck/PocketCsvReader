using PocketCsvReader.Configuration;

namespace PocketCsvReader.KeyValue.Configuration;

public abstract class KeyValueReaderBuilder<TBuilder> : ReaderBuilder<TBuilder>
    where TBuilder : KeyValueReaderBuilder<TBuilder>
{
    private readonly KeyValueFormat _format;
    private ProjectedColumn[]? _projection;

    protected KeyValueReaderBuilder(KeyValueFormat format)
        : base(KeyValueProfile.DefaultParserOptimizations)
        => _format = format;

    /// <summary>Selects and names the output columns in configuration order.</summary>
    public TBuilder WithProjection(Func<ColumnProjectionBuilder, ColumnProjectionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _projection = configure(new ColumnProjectionBuilder()).Build();
        return (TBuilder)this;
    }

    protected sealed override ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options with { ReadAhead = false, RowCountAtStart = false };

    protected KeyValueReader BuildReader()
        => new(new KeyValueProfile(
            _format,
            BuildSchema(),
            BuildResource(),
            BuildParsers(),
            BuildParserOptimizations()) { Projection = _projection, Filters = BuildFilters(), Skip = Skip, Take = Take });
}
