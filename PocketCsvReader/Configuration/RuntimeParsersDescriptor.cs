using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class RuntimeParsersDescriptor : IEnumerable<KeyValuePair<Type, ParseFunction>>
{
    private Dictionary<Type, (ParseFunction Legacy, Delegate? Span, Delegate Registration)> Parsers { get; } = [];
    private Dictionary<Type, Func<string?, IFormatProvider?, Delegate>>? _formatParsers;

    private Dictionary<Type, Func<string?, MemoryFieldParser>>? _memoryParsers;
    private Dictionary<Type, Delegate>? _utf8Parsers;

    internal bool HasMemoryParsers => _memoryParsers is not null;
    internal bool HasUtf8Parsers => _utf8Parsers is not null;

    internal bool HasParser(Type type) => Parsers.ContainsKey(type);

    internal void AddUtf8Parser<T>(TryParseUtf8<T> parser)
        => (_utf8Parsers ??= [])[typeof(T)] = parser;

    internal TryParseUtf8<T>? FindUtf8Parser<T>()
        => (TryParseUtf8<T>?)_utf8Parsers?.GetValueOrDefault(typeof(T));

    internal void AddDefaultMemoryParser<T>(Func<string?, ParseMemory<T>> factory)
        where T : notnull
    {
        (_memoryParsers ??= [])[typeof(T)] = pattern =>
        {
            var parse = factory(pattern);
            return new MemoryFieldParser<T>(parse, memory => parse(memory));
        };
        if (!Parsers.ContainsKey(typeof(T)))
        {
            var parse = factory(null);
            Parsers.Add(typeof(T), (text => parse(text.AsMemory()), null, parse));
        }
    }

    internal Func<string?, MemoryFieldParser>? FindMemoryParser(Type type)
        => _memoryParsers?.GetValueOrDefault(type);

    internal void AddDefaultParser<T>(ParseSpan<T> parse,
        Func<string?, IFormatProvider?, ParseSpan<T>>? formatParser)
    {
        AddParser(parse);
        if (formatParser is not null)
            (_formatParsers ??= [])[typeof(T)] = (pattern, culture) => formatParser(pattern, culture);
    }

    internal bool TryCreateFormattedParser(Type type, IFormatDescriptor? format, out Delegate? parser)
    {
        parser = null;
        if (_formatParsers is null || !_formatParsers.TryGetValue(type, out var factory))
            return false;
        var pattern = format switch
        {
            CustomFormatDescriptor custom => custom.Pattern,
            TemporalFormatDescriptor temporal => temporal.Pattern,
            _ => null
        };
        parser = factory(pattern, (format as ICultureFormatDescriptor)?.Culture);
        return true;
    }

    internal IEnumerable<KeyValuePair<Type, Delegate>> EnumerateSpanParsers()
    {
        foreach (var (type, parser) in Parsers)
        {
            if (parser.Span is { } span)
                yield return new(type, span);
        }
    }

    internal (Delegate Span, Delegate Registration)? FindParser(Type type)
        => Parsers.TryGetValue(type, out var parser) && parser.Span is { } span ? (span, parser.Registration) : null;

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public void AddParser<T>(ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        // Retain the existing enumerable contract for clients inspecting registrations.
        Parsers.Add(typeof(T), (text => parse(text.AsSpan())!, parse, parse));
    }

    public void AddParser<T>(ParseFunction<T> parse)
    {
        // Only the public enumerable needs an object-returning compatibility adapter.
        // Keep the typed result intact when adapting string parsers for reader access.
        ParseSpan<T> span = text => parse(text.ToString());
        Parsers.Add(typeof(T), (text => parse(text)!, span, parse));
    }

    public void AddParser(Type type, ParseFunction parse)
        => AddLegacyParser(type, parse, parse);

    private void AddLegacyParser(Type type, ParseFunction parse, Delegate registration)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        Parsers.Add(type, (parse, SpanParser.Adapt(type, parse), registration));
    }

    public int Count => Parsers.Count;

    public IEnumerator<KeyValuePair<Type, ParseFunction>> GetEnumerator()
        => Parsers.Select(parser => new KeyValuePair<Type, ParseFunction>(parser.Key, parser.Value.Legacy)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
