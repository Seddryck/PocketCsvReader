namespace PocketCsvReader.Configuration;

public abstract class ReaderBuilder<TBuilder>
    where TBuilder : ReaderBuilder<TBuilder>
{
    private readonly ProfileBuilderComponents _components = new();
    private readonly List<ReaderFilter> _filters = [];

    protected int Skip { get; private set; }
    protected int? Take { get; private set; }

    /// <summary>Skips the specified number of accepted data records before returning results.</summary>
    public TBuilder WithSkip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Skip = count;
        return (TBuilder)this;
    }

    /// <summary>Returns at most the specified number of data records after skipping.</summary>
    public TBuilder WithTake(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Take = count;
        return (TBuilder)this;
    }

    /// <summary>Keeps rows whose sanitized cell equals the expected text. Null never matches.</summary>
    public TBuilder WithFilter(string column, string expected, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!Enum.IsDefined(comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
        return WithFilter(column, value => value.Equals(expected.AsSpan(), comparison));
    }

    public TBuilder WithFilter(int ordinal, string expected, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!Enum.IsDefined(comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
        return WithFilter(ordinal, value => value.Equals(expected.AsSpan(), comparison));
    }

    /// <summary>Keeps rows accepted by a sanitized-span predicate. The span is valid only during the call.</summary>
    public TBuilder WithFilter(string column, ParseSpan<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(predicate);
        _filters.Add(new(column, 0, predicate, null));
        return (TBuilder)this;
    }

    public TBuilder WithFilter(int ordinal, ParseSpan<bool> predicate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentNullException.ThrowIfNull(predicate);
        _filters.Add(new(null, ordinal, predicate, null));
        return (TBuilder)this;
    }

    /// <summary>Keeps rows accepted by a typed predicate using the getter's parsing rules.</summary>
    public TBuilder WithFilter<T>(string column, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(predicate);
        _filters.Add(new(column, 0, null, (row, i) => Accept(row, i, predicate)));
        return (TBuilder)this;
    }

    public TBuilder WithFilter<T>(int ordinal, Func<T, bool> predicate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentNullException.ThrowIfNull(predicate);
        _filters.Add(new(null, ordinal, null, (row, i) => Accept(row, i, predicate)));
        return (TBuilder)this;
    }

    private static bool Accept<T>(IRowView row, int ordinal, Func<T, bool> predicate)
        => !(typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null && row.IsDBNull(ordinal))
            && predicate(row.GetFieldValue<T>(ordinal));

    /// <summary>Adds a read-only row predicate. Conditions run in registration order and short-circuit.</summary>
    public TBuilder WithRowFilter(Func<IRowView, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _filters.Add(new(predicate));
        return (TBuilder)this;
    }

    protected IReadOnlyList<ReaderFilter> BuildFilters() => Array.AsReadOnly(_filters.ToArray());

    private bool? _trackPosition;

    /// <summary>Explicitly enables or disables source position tracking.</summary>
    public TBuilder WithPositionTracking(bool enabled = true)
    {
        _trackPosition = enabled;
        return (TBuilder)this;
    }

    protected ReaderBuilder(ParserOptimizationOptions? defaultParserOptimizations = null)
    {
        if (defaultParserOptimizations is not null)
            _components.ConfigureParserOptimizations(defaultParserOptimizations);
    }

    public TBuilder WithSchema(Func<SchemaDescriptorBuilder, ISchemaDescriptorBuilder> configure)
    {
        _components.ConfigureSchema(configure);
        return (TBuilder)this;
    }

    public TBuilder WithSchema(ISchemaDescriptorBuilder schemaBuilder)
    {
        _components.ConfigureSchema(schemaBuilder);
        return (TBuilder)this;
    }

    public TBuilder WithSchema(SchemaDescriptor schema)
    {
        _components.ConfigureSchema(schema);
        return (TBuilder)this;
    }

    public TBuilder WithResource(Func<ResourceDescriptorBuilder, ResourceDescriptorBuilder> configure)
    {
        _components.ConfigureResource(configure);
        return (TBuilder)this;
    }

    public TBuilder WithResource(ResourceDescriptorBuilder resourceBuilder)
    {
        _components.ConfigureResource(resourceBuilder);
        return (TBuilder)this;
    }

    public TBuilder WithParsers(Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> configure)
    {
        _components.ConfigureParsers(configure);
        return (TBuilder)this;
    }

    public TBuilder WithParsers(RuntimeParsersDescriptorBuilder parserBuilder)
    {
        _components.ConfigureParsers(parserBuilder);
        return (TBuilder)this;
    }

    public TBuilder WithParserOptimizations(ParserOptimizationOptions options)
    {
        _components.ConfigureParserOptimizations(options);
        return (TBuilder)this;
    }

    protected SchemaDescriptor? BuildSchema() => _components.BuildSchema();
    protected ResourceDescriptor? BuildResource() => _components.BuildResource();
    protected RuntimeParsersDescriptor? BuildParsers() => _components.BuildParsers();

    protected ParserOptimizationOptions BuildParserOptimizations()
        => NormalizeParserOptimizations(_trackPosition.HasValue
            ? _components.ParserOptimizations with { TrackPosition = _trackPosition.Value }
            : _components.ParserOptimizations);

    protected virtual ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options;
}
