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
            if (expected.Length > 2)
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
