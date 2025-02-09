using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
internal class TypeIndexer
{
    private readonly Dictionary<Type, Func<int, object>> _typeToFunctionMap = new();

    public void Register<T>(Func<int, T> func)
        => Register(typeof(T), (int i) => func.Invoke(i)!);

    public void Register(Type type, Func<int, object> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _typeToFunctionMap[type] = func;
    }

    public bool TryGetParser<T>([NotNullWhen(true)] out Func<int, T>? func)
    {
        if (_typeToFunctionMap.TryGetValue(typeof(T), out var value))
        {
            func = (int i) => (T)value(i);
            return true;
        }
        func = null;
        return false;
    }

    public bool TryGetParser(Type type, [NotNullWhen(true)] out Func<int, object>? func)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToFunctionMap.TryGetValue(type, out func))
            return true;
        func = null;
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
                ?? throw new InvalidOperationException($"No function returning a type registered {typeof(T).Name} for type {typeof(T).Name}"); ;

        throw new InvalidOperationException($"No function registered for type {typeof(T).Name}");
    }
}
