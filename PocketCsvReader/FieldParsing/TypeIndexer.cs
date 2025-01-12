using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
public class TypeIndexer
{
    private readonly Dictionary<Type, object> _typeToFunctionMap = new();

    public void Register<T>(Func<int, T> func)
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        _typeToFunctionMap[typeof(T)] = func;
    }

    public bool TryGetFunction<T>([NotNullWhen(true)] out Func<int, T>? func)
    {
        if (_typeToFunctionMap.TryGetValue(typeof(T), out var value))
        {
            func = (Func<int, T>)value;
            return true;
        }
        func = null;
        return false;
    }

    public bool TryGetFunction(Type type, [NotNullWhen(true)] out Delegate? dlg)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToFunctionMap.TryGetValue(type, out var func))
        {
            dlg = (Delegate)func;
            return true;
        }
        dlg = null;
        return false;
    }


    public Delegate GetFunction(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeToFunctionMap.TryGetValue(type, out var func))
            return (Delegate)func;

        throw new InvalidOperationException($"No function registered for type {type.Name}");
    }

    public Func<int, T> GetFunction<T>()
    {
        if (_typeToFunctionMap.TryGetValue(typeof(T), out var func))
            return (Func<int, T>)func;

        throw new InvalidOperationException($"No function registered for type {typeof(T).Name}");
    }
}
