using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;

internal interface ITypeParserLocator
{
    Func<string, object> Locate(object[] parameters);
    Delegate? LocateTryParser(object[] parameters);
}

internal class TypeParserLocator<T> : ITypeParserLocator
{
    private Type Target { get; set; }

    public TypeParserLocator()
        => Target = typeof(T);

    public Func<string, T> Locate(object[] parameters)
    {
        var method = FindMethod(parameters.Select(x => x.GetType()).ToArray());
        var fixedParameters = new List<object>(parameters[0..(method.GetParameters().Length-1)]);
        return (string span) => (T)(method.Invoke(null, fixedParameters.Prepend(span).ToArray())
                                                ?? throw new InvalidOperationException());
    }

    public Delegate? LocateTryParser(object[] parameters)
    {
        // Pair with the exact Parse overload selected above, including any parameter fallback.
        var parse = FindMethod(parameters.Select(x => x.GetType()).ToArray());
        var arguments = parse.GetParameters();
        var signature = arguments.Select(x => x.ParameterType).Append(typeof(T).MakeByRefType()).ToArray();
        var method = Target.GetMethod(parse.Name == "ParseExact" ? "TryParseExact" : "TryParse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, null, signature, null);
        if (method is null || method.ReturnType != typeof(bool) || !method.GetParameters()[^1].IsOut)
            return null;

        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var value = Expression.Parameter(typeof(T).MakeByRefType(), "value");
        var text = Expression.Call(span, typeof(ReadOnlySpan<char>).GetMethod("ToString", Type.EmptyTypes)!);
        var fixedArguments = arguments.Skip(1).Select((argument, index) =>
            (Expression)Expression.Constant(parameters[index], argument.ParameterType));
        var call = Expression.Call(method, new Expression[] { text }.Concat(fixedArguments).Append(value));
        return Expression.Lambda<TryParseSpan<T>>(call, span, value).Compile();
    }

    private MethodInfo FindMethod(Type[] expected)
    {
        var methods = Target.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                        .Where(x => x.Name == "Parse" || x.Name == "ParseExact");

        var matching = new List<MethodInfo>();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                continue;
            if (!(new Type[] { typeof(string) }.Any(x => parameters[0].ParameterType == x)))
                continue;
            if (parameters.Length - 1 != expected.Length)
                continue;
            if (parameters.Skip(1).Select((param, index) => param.ParameterType.IsAssignableFrom(expected[index])).All(x => x))
                matching.Add(method);
        }

        if (matching.Count == 0)
        {
            if (expected.Length > 0)
                return FindMethod(expected[0..(expected.Length-1)]);
            throw new InvalidOperationException($"No method found for type {Target.Name}");
        }

        var match = matching.Count > 1 ? matching.First(x => x.GetParameters()[0].GetType() == typeof(string)) : matching[0];
        return match;
    }

    Func<string, object> ITypeParserLocator.Locate(object[] parameters)
    {
        var parser = Locate(parameters) ?? throw new InvalidOperationException();
        return (string input) => parser(input)!;
    }
}
