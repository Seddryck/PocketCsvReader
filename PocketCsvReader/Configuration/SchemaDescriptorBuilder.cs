using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class SchemaDescriptorBuilder
{
    private SchemaDescriptor Descriptor { get; set; } = new();

    public SchemaDescriptorBuilder WithField<T>()
        => WithField(typeof(T), null);

    public SchemaDescriptorBuilder WithField<T>(string name)
        => WithField(typeof(T), name);

    public SchemaDescriptorBuilder WithField(Type type, string? name = null)
    {
        var fields = Descriptor.Fields?.ToList() ?? [];
        fields.Add(new FieldDescriptor(type, name));
        return (Descriptor = Descriptor with { Fields = [.. fields] }, Builder: this).Builder;
    }

    public SchemaDescriptor? Build()
        => (Descriptor.Fields?.Length ?? 0) > 0 ? Descriptor : null;
}
