using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;

internal delegate T ParseSpan<T>(ReadOnlySpan<char> span);

internal class TypeIndexer
{
    private readonly Dictionary<Type, Delegate> _typeToFunctionMap = new();

    public void Register<T>(ParseSpan<T> parse)
        => Register(typeof(T), parse);

    protected void Register(Type type, Delegate parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        _typeToFunctionMap[type] = parse;
    }

    public bool TryGetParser<T>([NotNullWhen(true)] out ParseSpan<T>? parse)
    {
        if (_typeToFunctionMap.TryGetValue(typeof(T), out var dlg))
        {
            parse = (ParseSpan<T>)dlg;
            return true;
        }
        parse = null;
        return false;
    }

    protected bool TryGetParser(Type type, [NotNullWhen(true)] out Delegate? dlg)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToFunctionMap.TryGetValue(type, out dlg))
            return true;
        dlg = null;
        return false;
    }


    public Delegate GetFunction(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToFunctionMap.TryGetValue(type, out var func))
            return func;

        throw new InvalidOperationException($"No function registered for type {type.Name}");
    }

    public Func<int, T> GetFunction<T>()
    {
        if (_typeToFunctionMap.TryGetValue(typeof(T), out var func))
            return (func as Func<int, T>)
                ?? throw new InvalidOperationException($"No function returning a type registered {typeof(T).Name} for type {typeof(T).Name}");

        throw new InvalidOperationException($"No function registered for type {typeof(T).Name}");
    }
}
