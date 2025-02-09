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
    private Dictionary<Type, ParseFunction> _types = new();
    private RuntimeParsersDescriptor? Descriptor { get; set; }

    public RuntimeParsersDescriptorBuilder WithParser<T>(ParseFunction<T> parse)
        => WithParser(typeof(T), (string str) => parse.Invoke(str)!);

    public RuntimeParsersDescriptorBuilder WithParser(Type type, ParseFunction parse)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        if (!_types.TryAdd(type, parse))
            _types[type] = parse;
        return this;
    }

    public RuntimeParsersDescriptor? Build()
    {
        Descriptor = new RuntimeParsersDescriptor();
        foreach (var type in _types)
            Descriptor.AddParser(type.Key, type.Value);
        return Descriptor.Count > 0 ? Descriptor : null;
    }
}
