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
    private Dictionary<Type, Action<RuntimeParsersDescriptor>>? _defaults;
    private RuntimeParsersDescriptor? Descriptor { get; set; }

    /// <summary>Registers an integration default. Explicit WithParser registrations always win.</summary>
    /// <param name="parse">Parser for fields without a format.</param>
    /// <param name="formatParser">Optional factory receiving the field's pattern and culture.</param>
    public RuntimeParsersDescriptorBuilder WithDefaultParser<T>(ParseSpan<T> parse,
        Func<string?, IFormatProvider?, ParseSpan<T>>? formatParser = null)
    {
        ArgumentNullException.ThrowIfNull(parse);
        (_defaults ??= [])[typeof(T)] = descriptor => descriptor.AddDefaultParser(parse, formatParser);
        return this;
    }

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
        if (_defaults is not null)
        {
            foreach (var parser in _defaults)
            {
                if (!_types.ContainsKey(parser.Key))
                    parser.Value(Descriptor);
            }
        }

        foreach (var type in _types)
            type.Value(Descriptor);
        return Descriptor.Count > 0 ? Descriptor : null;
    }
}
