using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public class FieldDescriptorBuilder
{
    protected Type _runtimeType;
    protected string? _format;
    protected string? _name;

    protected internal FieldDescriptorBuilder(Type runtimeType)
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

    public virtual FieldDescriptor Build()
    {
        return new FieldDescriptor(_runtimeType, _name, _format);
    }
}
