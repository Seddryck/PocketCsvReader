namespace PocketCsvReader.Configuration;

internal sealed class ProfileBuilderComponents
{
    private ISchemaDescriptorBuilder? _schemaBuilder;
    private SchemaDescriptor? _schema;
    private ResourceDescriptorBuilder? _resourceBuilder;
    private RuntimeParsersDescriptorBuilder? _parserBuilder;

    public ParserOptimizationOptions ParserOptimizations { get; private set; } = new();

    public void ConfigureSchema(Func<SchemaDescriptorBuilder, ISchemaDescriptorBuilder> configure)
    {
        _schema = null;
        _schemaBuilder = configure(new SchemaDescriptorBuilder());
    }

    public void ConfigureSchema(ISchemaDescriptorBuilder builder)
    {
        _schema = null;
        _schemaBuilder = builder;
    }

    public void ConfigureSchema(SchemaDescriptor schema)
    {
        _schemaBuilder = null;
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
    }

    public void ConfigureResource(Func<ResourceDescriptorBuilder, ResourceDescriptorBuilder> configure)
        => _resourceBuilder = configure(new ResourceDescriptorBuilder());

    public void ConfigureResource(ResourceDescriptorBuilder builder)
        => _resourceBuilder = builder;

    public void ConfigureParsers(Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> configure)
        => _parserBuilder = configure(new RuntimeParsersDescriptorBuilder());

    public void ConfigureParsers(RuntimeParsersDescriptorBuilder builder)
        => _parserBuilder = builder;

    public void ConfigureParserOptimizations(ParserOptimizationOptions options)
        => ParserOptimizations = options ?? throw new ArgumentNullException(nameof(options));

    public SchemaDescriptor? BuildSchema() => _schema ?? _schemaBuilder?.Build();
    public ResourceDescriptor? BuildResource() => _resourceBuilder?.Build();
    public RuntimeParsersDescriptor? BuildParsers() => _parserBuilder?.Build();
}
