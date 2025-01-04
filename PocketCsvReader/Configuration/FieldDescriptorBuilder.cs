using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class FieldDescriptorBuilder
{
    private Type _runtimeType;
    private string? _format;
    private string? _name;

    internal FieldDescriptorBuilder(Type runtimeType)
    {
        _runtimeType = runtimeType;
    }

    public FieldDescriptorBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public FieldDescriptorBuilder WithFormat(string format)
    {
        _format = format;
        return this;
    }

    public FieldDescriptor Build()
    {
        return new FieldDescriptor(_runtimeType, _name, _format);
    }
}
