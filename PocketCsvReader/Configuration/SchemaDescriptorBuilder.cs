using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PocketCsvReader.Configuration;

public class SchemaDescriptorBuilder
{
    internal const string CustomFieldObsoleteMessage =
        "WithCustomField will be removed in 3.0. Use WithField instead.";

    public IndexedSchemaDescriptorBuilder Indexed()
        => new();
    public NamedSchemaDescriptorBuilder Named()
        => new();
}

public interface ISchemaDescriptorBuilder
{
    SchemaDescriptor? Build();
    ISchemaDescriptorBuilder WithField(Type type, string name);
    ISchemaDescriptorBuilder WithField(Type type, string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func);
    ISchemaDescriptorBuilder WithIntegerField(Type type, string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder> func);
    ISchemaDescriptorBuilder WithNumberField(Type type, string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder> func);
    ISchemaDescriptorBuilder WithTemporalField(Type type, string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder> func);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder WithCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder> func);
    ISchemaDescriptorBuilder WithIntegerField(Type type, string name);
    ISchemaDescriptorBuilder WithNumberField(Type type, string name);
    ISchemaDescriptorBuilder WithTemporalField(Type type, string name);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder WithCustomField(Type type, string name);
}

public class IndexedSchemaDescriptorBuilder : ISchemaDescriptorBuilder
{
    private List<FieldDescriptorBuilder> _fields = new();
    private SchemaDescriptor Descriptor { get; set; } = new SchemaDescriptor.IndexedSchemaDescriptor();

    public IndexedSchemaDescriptorBuilder WithField<T>()
        => WithField<T>(x => x);

    public IndexedSchemaDescriptorBuilder WithField<T>(Func<FieldDescriptorBuilder, FieldDescriptorBuilder>? func = null)
        => WithField(typeof(T), func);

    public IndexedSchemaDescriptorBuilder WithField(Type type)
        => WithField(type, x => x);

    public IndexedSchemaDescriptorBuilder WithField(Type type, Func<FieldDescriptorBuilder, FieldDescriptorBuilder>? func = null)
    {
        var builder = new FieldDescriptorBuilder(type);
        _fields.Add(func?.Invoke(builder) ?? builder);
        return this;
    }
    public IndexedSchemaDescriptorBuilder WithField(Type type, string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder>? func = null)
        => WithField(type, (builder) => (func?.Invoke(builder) ?? builder).WithName(name));

    public IndexedSchemaDescriptorBuilder WithIntegerField<T>(Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder>? func = null)
        => WithIntegerField(typeof(T), func);

    public IndexedSchemaDescriptorBuilder WithIntegerField(Type type, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder>? func = null)
    {
        var builder = new IntegerFieldDescriptorBuilder(type);
        _fields.Add(func?.Invoke(builder) ?? builder);
        return this;
    }

    public IndexedSchemaDescriptorBuilder WithIntegerField(Type type, string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder>? func = null)
        => WithIntegerField(type, (builder) => (func?.Invoke(builder) ?? builder).WithName(name));

    public IndexedSchemaDescriptorBuilder WithNumberField<T>(Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder>? func = null)
        => WithNumberField(typeof(T), func);

    public IndexedSchemaDescriptorBuilder WithNumberField(Type type, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder>? func = null)
    {
        var builder = new NumberFieldDescriptorBuilder(type);
        _fields.Add(func?.Invoke(builder) ?? builder);
        return this;
    }

    public IndexedSchemaDescriptorBuilder WithNumberField(Type type, string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder>? func = null)
        => WithNumberField(type, (builder) => (func?.Invoke(builder) ?? builder).WithName(name));

    public IndexedSchemaDescriptorBuilder WithTemporalField(Type type, string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder>? func = null)
        => WithTemporalField(type, (builder) => (func?.Invoke(builder) ?? builder).WithName(name));

    public IndexedSchemaDescriptorBuilder WithTemporalField<T>(Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder>? func = null)
        => WithTemporalField(typeof(T), func);

    public IndexedSchemaDescriptorBuilder WithTemporalField(Type type, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder>? func = null)
    {
        var builder = new TemporalFieldDescriptorBuilder(type);
        _fields.Add(func?.Invoke(builder) ?? builder);
        return this;
    }

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public IndexedSchemaDescriptorBuilder WithCustomField(Type type, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func = null)
        => AddCustomField(type, func);

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public IndexedSchemaDescriptorBuilder WithCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func = null)
        => AddCustomField(type, name, func);

    private IndexedSchemaDescriptorBuilder AddCustomField(Type type, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func)
    {
        var builder = new CustomFieldDescriptorBuilder(type);
        _fields.Add(func?.Invoke(builder) ?? builder);
        return this;
    }

    private IndexedSchemaDescriptorBuilder AddCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func)
        => AddCustomField(type, (builder) => (func?.Invoke(builder) ?? builder).WithName(name));

    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithField(Type type, string name)
        => WithField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithField(Type type, string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func)
        => WithField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithIntegerField(Type type, string name)
        => WithIntegerField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithIntegerField(Type type, string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder> func)
        => WithIntegerField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithNumberField(Type type, string name)
        => WithNumberField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithNumberField(Type type, string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder> func)
        => WithNumberField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithTemporalField(Type type, string name)
        => WithTemporalField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithTemporalField(Type type, string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder> func)
        => WithTemporalField(type, name, func);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithCustomField(Type type, string name)
        => AddCustomField(type, name, null);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder> func)
        => AddCustomField(type, name, func);

    public SchemaDescriptor? Build()
    {
        foreach (var field in _fields)
            Descriptor.Fields.Add(field.Build());
        return (Descriptor.Fields?.Length ?? 0) > 0 ? Descriptor : null;
    }
}

public class NamedSchemaDescriptorBuilder : ISchemaDescriptorBuilder
{
    private List<FieldDescriptorBuilder> _fields = new();

    private SchemaDescriptor Descriptor { get; set; } = new SchemaDescriptor.NamedSchemaDescriptor();

    public NamedSchemaDescriptorBuilder WithField<T>(string name)
        => WithField<T>(name, x => x);

    public NamedSchemaDescriptorBuilder WithField<T>(string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder>? func = null)
        => WithField(typeof(T), name, func);

    public NamedSchemaDescriptorBuilder WithField(Type type, string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder>? func = null)
    {
        var builder = new FieldDescriptorBuilder(type);
        _fields.Add((func?.Invoke(builder) ?? builder).WithName(name));
        return this;
    }

    public NamedSchemaDescriptorBuilder WithIntegerField<T>(string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder>? func = null)
        => WithIntegerField(typeof(T), name, func);

    public NamedSchemaDescriptorBuilder WithIntegerField(Type type, string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder>? func = null)
    {
        var builder = new IntegerFieldDescriptorBuilder(type);
        _fields.Add((func?.Invoke(builder) ?? builder).WithName(name));
        return this;
    }

    public NamedSchemaDescriptorBuilder WithNumberField<T>(string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder>? func = null)
        => WithNumberField(typeof(T), name, func);

    public NamedSchemaDescriptorBuilder WithNumberField(Type type, string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder>? func = null)
    {
        var builder = new NumberFieldDescriptorBuilder(type);
        _fields.Add((func?.Invoke(builder) ?? builder).WithName(name));
        return this;
    }

    public NamedSchemaDescriptorBuilder WithTemporalField<T>(string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder>? func = null)
        => WithTemporalField(typeof(T), name, func);

    public NamedSchemaDescriptorBuilder WithTemporalField(Type type, string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder>? func = null)
    {
        var builder = new TemporalFieldDescriptorBuilder(type);
        _fields.Add((func?.Invoke(builder) ?? builder).WithName(name));
        return this;
    }

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public NamedSchemaDescriptorBuilder WithCustomField<T>(string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func = null)
        => AddCustomField(typeof(T), name, func);

    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    public NamedSchemaDescriptorBuilder WithCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func = null)
        => AddCustomField(type, name, func);

    private NamedSchemaDescriptorBuilder AddCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder>? func)
    {
        var builder = new CustomFieldDescriptorBuilder(type);
        _fields.Add((func?.Invoke(builder) ?? builder).WithName(name));
        return this;
    }

    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithField(Type type, string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func)
        => WithField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithIntegerField(Type type, string name, Func<IntegerFieldDescriptorBuilder, IntegerFieldDescriptorBuilder> func)
        => WithIntegerField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithNumberField(Type type, string name, Func<NumberFieldDescriptorBuilder, NumberFieldDescriptorBuilder> func)
        => WithNumberField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithTemporalField(Type type, string name, Func<TemporalFieldDescriptorBuilder, TemporalFieldDescriptorBuilder> func)
        => WithTemporalField(type, name, func);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithCustomField(Type type, string name, Func<CustomFieldDescriptorBuilder, CustomFieldDescriptorBuilder> func)
        => AddCustomField(type, name, func);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithField(Type type, string name)
        => WithField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithIntegerField(Type type, string name)
        => WithIntegerField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithNumberField(Type type, string name)
        => WithNumberField(type, name, null);
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithTemporalField(Type type, string name)
        => WithTemporalField(type, name, null);
    [Obsolete(SchemaDescriptorBuilder.CustomFieldObsoleteMessage)]
    ISchemaDescriptorBuilder ISchemaDescriptorBuilder.WithCustomField(Type type, string name)
        => AddCustomField(type, name, null);

    public SchemaDescriptor? Build()
    {
        foreach (var field in _fields)
            Descriptor.Fields.Add(field.Build());
        return (Descriptor.Fields?.Length ?? 0) > 0 ? Descriptor : null;
    }
}
