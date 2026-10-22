using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class RuntimeParsersDescriptor : IEnumerable<KeyValuePair<Type, ParseFunction>>
{
    private Dictionary<Type, (ParseFunction Legacy, Delegate Span)> Parsers { get; } = [];
    private Dictionary<Type, Func<string?, IFormatProvider?, Delegate>>? _formatParsers;

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

    internal IEnumerable<KeyValuePair<Type, Delegate>> SpanParsers
        => Parsers.Select(parser => new KeyValuePair<Type, Delegate>(parser.Key, parser.Value.Span));

    [System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
    public void AddParser<T>(ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        // Retain the existing enumerable contract for clients inspecting registrations.
        Parsers.Add(typeof(T), (text => parse(text.AsSpan())!, parse));
    }

    public void AddParser<T>(ParseFunction<T> parse)
        => AddParser(typeof(T), (string str) => parse.Invoke(str)!);

    public void AddParser(Type type, ParseFunction parse)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        Parsers.Add(type, (parse, SpanParser.Adapt(type, parse)));
    }

    public int Count => Parsers.Count;

    public IEnumerator<KeyValuePair<Type, ParseFunction>> GetEnumerator()
        => Parsers.Select(parser => new KeyValuePair<Type, ParseFunction>(parser.Key, parser.Value.Legacy)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
