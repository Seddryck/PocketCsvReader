using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

internal delegate T ParseSpan<T>(ReadOnlySpan<char> span);
internal class SpanParser
{
    private Dictionary<Type, Delegate> TypeParsers { get; } = new();
    private Dictionary<int, Delegate> FieldParsers { get; } = new();

    public SpanParser()
    {
        Register(s => s.ToString());
        Register(s => s[0]);
        Register(Guid.Parse);
        Register(bool.Parse);
        Register((s) => byte.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => short.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => int.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => long.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => decimal.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => float.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => double.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => DateTime.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => DateOnly.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => TimeOnly.Parse(s, CultureInfo.InvariantCulture));
        Register((s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture));
    }

    public void Register<T>(ParseSpan<T> parse)
        => Register(typeof(T), parse);

    public void Register(Type type, Delegate dlg)
    {
        ArgumentNullException.ThrowIfNull(dlg);
        if (!IsParseSpan(dlg, out var parseType) || type != parseType)
            throw new ArgumentException("Unexpected delegate.", nameof(dlg));
        TypeParsers[type] = dlg;
    }

    public void Register<T>(int index, ParseSpan<T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        FieldParsers[index] = parse;
    }

    public void Register(int index, Type type, Delegate dlg)
    {
        ArgumentNullException.ThrowIfNull(dlg);
        if (!IsParseSpan(dlg, out var parseType) || type != parseType)
            throw new ArgumentException("Unexpected delegate.", nameof(dlg));
        FieldParsers[index] = dlg;
    }

    private static bool IsParseSpan(Delegate dlg, out Type parsedType)
    {
        var t = dlg.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ParseSpan<>))
        {
            parsedType = t.GetGenericArguments()[0];
            return true;
        }

        parsedType = null!;
        return false;
    }

    public bool TryGetParser<T>([NotNullWhen(true)] out ParseSpan<T>? parse)
    {
        if (TypeParsers.TryGetValue(typeof(T), out var dlg))
        {
            if (dlg is not ParseSpan<T>)
                throw new InvalidOperationException();
            parse = (ParseSpan<T>)dlg!;
            return true;
        }
        parse = null;
        return false;
    }

    public bool TryGetParser(Type type, [NotNullWhen(true)] out ParseSpan<object>? parse)
    {
        if (TypeParsers.TryGetValue(type, out var dlg))
        {
            parse = BoxParser(dlg);
            return true;
        }
        parse = null;
        return false;
    }

    public bool TryGetParser<T>(int index, [NotNullWhen(true)] out ParseSpan<T>? parse)
    {
        if (FieldParsers.TryGetValue(index, out var dlg) && dlg is ParseSpan<T>)
        {
            parse = (ParseSpan<T>)dlg!;
            return true;
        }
        parse = null;
        return false;
    }

    public ParseSpan<T> GetParser<T>(int index)
    {
        if (FieldParsers.TryGetValue(index, out var dlg))
        {
            if (dlg is not ParseSpan<T>)
            {
                if (dlg.GetType().IsGenericType && dlg.GetType().GetGenericTypeDefinition() == typeof(ParseSpan<>))
                    throw new InvalidOperationException($"The parser was expected to be a ParseSpan<{typeof(T).Name}> but was a ParseSpan<{dlg.GetType().GetGenericArguments()[0].Name}>");
                else
                    throw new InvalidOperationException($"The parser was expected to be a ParseSpan<{typeof(T).Name}> but was a {dlg.GetType().Name}");
            }

            return (ParseSpan<T>)dlg!;
        }
        throw new InvalidOperationException($"No parser registered for field index '{index}'");
    }

    public bool TryGetParser(int index, [NotNullWhen(true)] out ParseSpan<object>? parse)
    {
        if (FieldParsers.TryGetValue(index, out var dlg))
        {
            parse = BoxParser(dlg);
            return true;
        }
        parse = null;
        return false;
    }

    private static ParseSpan<object> BoxParser(Delegate parse)
    {
        var type = parse.GetType();

        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ParseSpan<>))
            throw new ArgumentException("Delegate must be of type ParseSpan<T>");

        var spanParam = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var delConst = Expression.Constant(parse, type);

        var invoke = Expression.Invoke(Expression.Convert(delConst, type), spanParam);
        var box = Expression.Convert(invoke, typeof(object));

        var lambda = Expression.Lambda<ParseSpan<object>>(box, spanParam);
        return lambda.Compile();
    }

    public T Parse<T>(ReadOnlySpan<char> span)
    {
        if (TryGetParser<T>(out var parse))
            return parse(span);
        throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
    }

    public T Parse<T>(int index, ReadOnlySpan<char> span)
    {
        if (TryGetParser<T>(index, out var parse))
            return parse(span);
        if (TryGetParser(out parse))
            return parse(span);
        throw new InvalidOperationException($"No parser registered for field with index {index}");
    }

    public bool TryParse<T>(int index, ReadOnlySpan<char> span, [NotNullWhen(true)] out T? value)
    {
        if (TryGetParser<T>(index, out var parse))
        {
            value = parse(span)!;
            return true;
        }
        value = default;
        return false;
    }

    public bool TryParse<T>(ReadOnlySpan<char> span, [NotNullWhen(true)] out T? value)
    {
        if (TryGetParser<T>(out var parse))
        {
            value = parse(span)!;
            return true;
        }
        value = default;
        return false;
    }

    private static readonly Lazy<SpanParser> _defaultParser = new(() =>
    {
        var p = new SpanParser();
        p.Register(s => s.ToString());
        p.Register(s => s[0]);
        p.Register(Guid.Parse);
        p.Register(bool.Parse);
        p.Register((s) => byte.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => short.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => int.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => long.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => decimal.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => float.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => double.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => DateTime.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => DateOnly.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => TimeOnly.Parse(s, CultureInfo.InvariantCulture));
        p.Register((s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture));
        return p;
    });

    public static SpanParser Default
        => _defaultParser.Value;
}
