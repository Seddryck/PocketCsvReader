namespace PocketCsvReader.Configuration;

/// <summary>Configures schema fields with builders that carry the field type.</summary>
/// <remarks>Existing instance overloads still accept callbacks that use the original builder API.</remarks>
public static class TypedSchemaDescriptorBuilderExtensions
{
    public static IndexedSchemaDescriptorBuilder WithField<T>(
        this IndexedSchemaDescriptorBuilder schema, Func<FieldDescriptorBuilder<T>, FieldDescriptorBuilder<T>> func)
        => schema.AddField(new FieldDescriptorBuilder<T>().Configure(func));

    public static IndexedSchemaDescriptorBuilder WithIntegerField<T>(
        this IndexedSchemaDescriptorBuilder schema, Func<IntegerFieldDescriptorBuilder<T>, IntegerFieldDescriptorBuilder<T>> func)
        => schema.AddField(new IntegerFieldDescriptorBuilder<T>().Configure(func));

    public static IndexedSchemaDescriptorBuilder WithNumberField<T>(
        this IndexedSchemaDescriptorBuilder schema, Func<NumberFieldDescriptorBuilder<T>, NumberFieldDescriptorBuilder<T>> func)
        => schema.AddField(new NumberFieldDescriptorBuilder<T>().Configure(func));

    public static IndexedSchemaDescriptorBuilder WithTemporalField<T>(
        this IndexedSchemaDescriptorBuilder schema, Func<TemporalFieldDescriptorBuilder<T>, TemporalFieldDescriptorBuilder<T>> func)
        => schema.AddField(new TemporalFieldDescriptorBuilder<T>().Configure(func));

    public static NamedSchemaDescriptorBuilder WithField<T>(
        this NamedSchemaDescriptorBuilder schema, string name, Func<FieldDescriptorBuilder<T>, FieldDescriptorBuilder<T>> func)
        => schema.AddField(name, new FieldDescriptorBuilder<T>().Configure(func));

    public static NamedSchemaDescriptorBuilder WithIntegerField<T>(
        this NamedSchemaDescriptorBuilder schema, string name, Func<IntegerFieldDescriptorBuilder<T>, IntegerFieldDescriptorBuilder<T>> func)
        => schema.AddField(name, new IntegerFieldDescriptorBuilder<T>().Configure(func));

    public static NamedSchemaDescriptorBuilder WithNumberField<T>(
        this NamedSchemaDescriptorBuilder schema, string name, Func<NumberFieldDescriptorBuilder<T>, NumberFieldDescriptorBuilder<T>> func)
        => schema.AddField(name, new NumberFieldDescriptorBuilder<T>().Configure(func));

    public static NamedSchemaDescriptorBuilder WithTemporalField<T>(
        this NamedSchemaDescriptorBuilder schema, string name, Func<TemporalFieldDescriptorBuilder<T>, TemporalFieldDescriptorBuilder<T>> func)
        => schema.AddField(name, new TemporalFieldDescriptorBuilder<T>().Configure(func));
}
