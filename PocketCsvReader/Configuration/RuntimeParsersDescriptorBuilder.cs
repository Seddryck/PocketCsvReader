using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PocketCsvReader.Configuration;

public class RuntimeParsersDescriptorBuilder
{
    private Dictionary<Type, Action<RuntimeParsersDescriptor>> _types = new();
    private RuntimeParsersDescriptor? Descriptor { get; set; }

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public RuntimeParsersDescriptorBuilder WithParser<T>(ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        _types[typeof(T)] = descriptor => descriptor.AddParser(parse);
        return this;
    }

    public RuntimeParsersDescriptorBuilder WithParser<T>(ParseFunction<T> parse)
        => WithParser(typeof(T), (string str) => parse.Invoke(str)!);

    public RuntimeParsersDescriptorBuilder WithParser(Type type, ParseFunction parse)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        _types[type] = descriptor => descriptor.AddParser(type, parse);
        return this;
    }

    public RuntimeParsersDescriptor? Build()
    {
        Descriptor = new RuntimeParsersDescriptor();
        foreach (var type in _types)
            type.Value(Descriptor);
        return Descriptor.Count > 0 ? Descriptor : null;
    }
}
