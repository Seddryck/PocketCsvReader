namespace PocketCsvReader.Configuration;

/// <summary>Configures fields declared with runtime types using fluent field builders.</summary>
public static class RuntimeSchemaDescriptorBuilderExtensions
{
    public static IndexedSchemaDescriptorBuilder WithField(
        this IndexedSchemaDescriptorBuilder schema, Type type,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithIntegerField(
        this IndexedSchemaDescriptorBuilder schema, Type type,
        Func<IntegerFieldDescriptorBuilder<object>, IntegerFieldDescriptorBuilder<object>> func)
        => schema.WithIntegerField(type, builder => (IntegerFieldDescriptorBuilder)new IntegerFieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithNumberField(
        this IndexedSchemaDescriptorBuilder schema, Type type,
        Func<NumberFieldDescriptorBuilder<object>, NumberFieldDescriptorBuilder<object>> func)
        => schema.WithNumberField(type, builder => (NumberFieldDescriptorBuilder)new NumberFieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithTemporalField(
        this IndexedSchemaDescriptorBuilder schema, Type type,
        Func<TemporalFieldDescriptorBuilder<object>, TemporalFieldDescriptorBuilder<object>> func)
        => schema.WithTemporalField(type, builder => (TemporalFieldDescriptorBuilder)new TemporalFieldDescriptorBuilder<object>(builder).Configure(func));

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public static IndexedSchemaDescriptorBuilder WithCustomField(
        this IndexedSchemaDescriptorBuilder schema, Type type,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithField(
        this IndexedSchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithIntegerField(
        this IndexedSchemaDescriptorBuilder schema, Type type, string name,
        Func<IntegerFieldDescriptorBuilder<object>, IntegerFieldDescriptorBuilder<object>> func)
        => schema.WithIntegerField(type, name, builder => (IntegerFieldDescriptorBuilder)new IntegerFieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithNumberField(
        this IndexedSchemaDescriptorBuilder schema, Type type, string name,
        Func<NumberFieldDescriptorBuilder<object>, NumberFieldDescriptorBuilder<object>> func)
        => schema.WithNumberField(type, name, builder => (NumberFieldDescriptorBuilder)new NumberFieldDescriptorBuilder<object>(builder).Configure(func));

    public static IndexedSchemaDescriptorBuilder WithTemporalField(
        this IndexedSchemaDescriptorBuilder schema, Type type, string name,
        Func<TemporalFieldDescriptorBuilder<object>, TemporalFieldDescriptorBuilder<object>> func)
        => schema.WithTemporalField(type, name, builder => (TemporalFieldDescriptorBuilder)new TemporalFieldDescriptorBuilder<object>(builder).Configure(func));

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public static IndexedSchemaDescriptorBuilder WithCustomField(
        this IndexedSchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static NamedSchemaDescriptorBuilder WithField(
        this NamedSchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static NamedSchemaDescriptorBuilder WithIntegerField(
        this NamedSchemaDescriptorBuilder schema, Type type, string name,
        Func<IntegerFieldDescriptorBuilder<object>, IntegerFieldDescriptorBuilder<object>> func)
        => schema.WithIntegerField(type, name, builder => (IntegerFieldDescriptorBuilder)new IntegerFieldDescriptorBuilder<object>(builder).Configure(func));

    public static NamedSchemaDescriptorBuilder WithNumberField(
        this NamedSchemaDescriptorBuilder schema, Type type, string name,
        Func<NumberFieldDescriptorBuilder<object>, NumberFieldDescriptorBuilder<object>> func)
        => schema.WithNumberField(type, name, builder => (NumberFieldDescriptorBuilder)new NumberFieldDescriptorBuilder<object>(builder).Configure(func));

    public static NamedSchemaDescriptorBuilder WithTemporalField(
        this NamedSchemaDescriptorBuilder schema, Type type, string name,
        Func<TemporalFieldDescriptorBuilder<object>, TemporalFieldDescriptorBuilder<object>> func)
        => schema.WithTemporalField(type, name, builder => (TemporalFieldDescriptorBuilder)new TemporalFieldDescriptorBuilder<object>(builder).Configure(func));

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public static NamedSchemaDescriptorBuilder WithCustomField(
        this NamedSchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static ISchemaDescriptorBuilder WithField(
        this ISchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));

    public static ISchemaDescriptorBuilder WithIntegerField(
        this ISchemaDescriptorBuilder schema, Type type, string name,
        Func<IntegerFieldDescriptorBuilder<object>, IntegerFieldDescriptorBuilder<object>> func)
        => schema.WithIntegerField(type, name, builder => (IntegerFieldDescriptorBuilder)new IntegerFieldDescriptorBuilder<object>(builder).Configure(func));

    public static ISchemaDescriptorBuilder WithNumberField(
        this ISchemaDescriptorBuilder schema, Type type, string name,
        Func<NumberFieldDescriptorBuilder<object>, NumberFieldDescriptorBuilder<object>> func)
        => schema.WithNumberField(type, name, builder => (NumberFieldDescriptorBuilder)new NumberFieldDescriptorBuilder<object>(builder).Configure(func));

    public static ISchemaDescriptorBuilder WithTemporalField(
        this ISchemaDescriptorBuilder schema, Type type, string name,
        Func<TemporalFieldDescriptorBuilder<object>, TemporalFieldDescriptorBuilder<object>> func)
        => schema.WithTemporalField(type, name, builder => (TemporalFieldDescriptorBuilder)new TemporalFieldDescriptorBuilder<object>(builder).Configure(func));

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public static ISchemaDescriptorBuilder WithCustomField(
        this ISchemaDescriptorBuilder schema, Type type, string name,
        Func<FieldDescriptorBuilder<object>, FieldDescriptorBuilder<object>> func)
        => schema.WithField(type, name, builder => new FieldDescriptorBuilder<object>(builder).Configure(func));
}
