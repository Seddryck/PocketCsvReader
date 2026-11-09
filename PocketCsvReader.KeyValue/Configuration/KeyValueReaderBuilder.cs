using PocketCsvReader.Configuration;

namespace PocketCsvReader.KeyValue.Configuration;

public abstract class KeyValueReaderBuilder<TBuilder> : ReaderBuilder<TBuilder>
    where TBuilder : KeyValueReaderBuilder<TBuilder>
{
    private readonly KeyValueFormat _format;
    private ProjectedColumn[]? _projection;
    private bool _stableFieldLayout;

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

    /// <summary>Reuses the first record's keys and order for subsequent records.</summary>
    /// <remarks>Field counts are checked. Renames and reordering with the same count may misassign values.</remarks>
    public TBuilder WithStableFieldLayout()
    {
        _stableFieldLayout = true;
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
            BuildParserOptimizations())
        {
            Projection = _projection,
            StableFieldLayout = _stableFieldLayout,
            Filters = BuildFilters(),
            Skip = Skip,
            Take = Take
        });
}
