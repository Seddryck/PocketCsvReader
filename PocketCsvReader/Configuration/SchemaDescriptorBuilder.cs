using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class SchemaDescriptorBuilder
{
    public IndexedSchemaDescriptorBuilder Indexed()
        => new();
    public NamedSchemaDescriptorBuilder Named()
        => new();
}

public interface ISchemaDescriptorBuilder
{
    SchemaDescriptor? Build();
}

public class IndexedSchemaDescriptorBuilder : ISchemaDescriptorBuilder
{
    private List<FieldDescriptorBuilder> _fields = new();
    private SchemaDescriptor Descriptor { get; set; } = new SchemaDescriptor.IndexedSchemaDescriptor();

    public IndexedSchemaDescriptorBuilder WithField<T>()
        => WithField<T>(x => x);

    public IndexedSchemaDescriptorBuilder WithField<T>(Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func)
        => WithField(typeof(T), func);

    public IndexedSchemaDescriptorBuilder WithField(Type type)
        => WithField(type, x => x);

    public IndexedSchemaDescriptorBuilder WithField(Type type, Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func)
    {
        _fields.Add(func(new FieldDescriptorBuilder(type)));
        return this;
    }

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

    public NamedSchemaDescriptorBuilder WithField<T>(string name, Func<FieldDescriptorBuilder, FieldDescriptorBuilder> func)
    {
        _fields.Add(func(new FieldDescriptorBuilder(typeof(T)).WithName(name)));
        return this;
    }

    public SchemaDescriptor? Build()
    {
        foreach (var field in _fields)
            Descriptor.Fields.Add(field.Build());
        return (Descriptor.Fields?.Length ?? 0) > 0 ? Descriptor : null;
    }
}
