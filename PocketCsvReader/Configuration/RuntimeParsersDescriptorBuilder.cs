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
    private Dictionary<Type, Action<RuntimeParsersDescriptor>>? _memoryDefaults;
    private Dictionary<Type, Action<RuntimeParsersDescriptor>>? _utf8Defaults;
    private RuntimeParsersDescriptor? Descriptor { get; set; }

    /// <summary>Registers an integration default. Explicit WithParser registrations always win.</summary>
    /// <param name="parse">Parser for fields without a format.</param>
    /// <param name="formatParser">Optional factory receiving the field's pattern and culture.</param>
    public RuntimeParsersDescriptorBuilder WithDefaultParser<T>(ParseSpan<T> parse,
        Func<string?, IFormatProvider?, ParseSpan<T>>? formatParser = null)
        => WithDefaultParser(parse, formatParser, null);

    internal RuntimeParsersDescriptorBuilder WithDefaultParser<T>(ParseSpan<T> parse,
        Func<string?, IFormatProvider?, ParseSpan<T>>? formatParser, TryParseUtf8<T>? utf8Parser)
    {
        ArgumentNullException.ThrowIfNull(parse);
        _memoryDefaults?.Remove(typeof(T));
        (_defaults ??= [])[typeof(T)] = descriptor =>
        {
            descriptor.AddDefaultParser(parse, formatParser);
            if (utf8Parser is not null)
                descriptor.AddUtf8Parser(utf8Parser);
        };
        return this;
    }

    internal RuntimeParsersDescriptorBuilder WithDefaultMemoryParser<T>(Func<string?, ParseMemory<T>> createParser)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(createParser);
        _defaults?.Remove(typeof(T));
        (_memoryDefaults ??= [])[typeof(T)] = descriptor => descriptor.AddDefaultMemoryParser(createParser);
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
    {
        ArgumentNullException.ThrowIfNull(parse);
        _types[typeof(T)] = descriptor => descriptor.AddParser(parse);
        return this;
    }

    public RuntimeParsersDescriptorBuilder WithParser(Type type, ParseFunction parse)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        _types[type] = descriptor => descriptor.AddParser(type, parse);
        return this;
    }

    /// <summary>Registers a runtime parser with a native UTF-8 fast path and a character fallback.</summary>
    /// <param name="utf8Parser">Parser that succeeds only when it consumes the complete field.</param>
    /// <param name="fallback">Equivalent character parser used when bytes are unavailable or the byte parser returns false.</param>
    public RuntimeParsersDescriptorBuilder WithUtf8Parser<T>(TryParseUtf8<T> utf8Parser, ParseSpan<T> fallback)
    {
        ArgumentNullException.ThrowIfNull(utf8Parser);
        ArgumentNullException.ThrowIfNull(fallback);
        _types[typeof(T)] = descriptor =>
        {
            descriptor.AddParser(fallback);
            descriptor.AddUtf8Parser(utf8Parser);
        };
        return this;
    }

    internal RuntimeParsersDescriptorBuilder WithDefaultUtf8Parser<T>(TryParseUtf8<T> parser)
    {
        (_utf8Defaults ??= [])[typeof(T)] = descriptor => descriptor.AddUtf8Parser(parser);
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
        if (_memoryDefaults is not null)
        {
            foreach (var parser in _memoryDefaults.Values)
                parser(Descriptor);
        }
        if (_utf8Defaults is not null)
        {
            foreach (var (type, register) in _utf8Defaults)
            {
                if (!Descriptor.HasParser(type))
                    register(Descriptor);
            }
        }
        return Descriptor.Count > 0 || Descriptor.HasUtf8Parsers ? Descriptor : null;
    }
}
