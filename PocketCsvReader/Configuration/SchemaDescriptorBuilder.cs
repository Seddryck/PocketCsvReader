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
    private SchemaDescriptor Descriptor { get; set; } = new SchemaDescriptor.IndexedSchemaDescriptor();

    public IndexedSchemaDescriptorBuilder WithField<T>()
        => WithField(typeof(T), null);

    public IndexedSchemaDescriptorBuilder WithField<T>(string name)
        => WithField(typeof(T), name);

    public IndexedSchemaDescriptorBuilder WithField(Type type, string? name = null)
    {
        Descriptor.Fields.Add(new FieldDescriptor(type, name));
        return this;
    }

    public SchemaDescriptor? Build()
        => (Descriptor.Fields.Length) > 0 ? Descriptor : null;
}

public class NamedSchemaDescriptorBuilder : ISchemaDescriptorBuilder
{
    private List<FieldDescriptor> _fields = new();

    private SchemaDescriptor Descriptor { get; set; } = new SchemaDescriptor.NamedSchemaDescriptor();

    public NamedSchemaDescriptorBuilder WithField<T>(string name)
        => WithField(typeof(T), name);

    public NamedSchemaDescriptorBuilder WithField(Type type, string name)
    {
        _fields.Add(new FieldDescriptor(type, name));
        return this;
    }

    public SchemaDescriptor? Build()
    {
        foreach (var field in _fields)
            Descriptor.Fields.Add(field);
        return (Descriptor.Fields?.Length ?? 0) > 0 ? Descriptor : null;
    }
}
