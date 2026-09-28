namespace PocketCsvReader.Configuration;

public abstract class ReaderBuilder<TBuilder>
    where TBuilder : ReaderBuilder<TBuilder>
{
    private readonly ProfileBuilderComponents _components = new();

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
        => NormalizeParserOptimizations(_components.ParserOptimizations);

    protected virtual ParserOptimizationOptions NormalizeParserOptimizations(ParserOptimizationOptions options)
        => options;
}
