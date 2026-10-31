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
    Delegate LocateSpanParser(object[] parameters);
    Delegate? LocateTryParser(object[] parameters);
    SpanParser.ParserEntry CreateEntry(Type type, object[] parameters);
}

internal class TypeParserLocator<T> : ITypeParserLocator
{
    private Type Target { get; set; }

    public TypeParserLocator()
        => Target = typeof(T);

    public Func<string, T> Locate(object[] parameters)
    {
        var parse = LocateSpanParser(parameters);
        return text => parse(text.AsSpan());
    }

    public ParseSpan<T> LocateSpanParser(object[] parameters)
    {
        var method = FindMethod(parameters.Select(x => x.GetType()).ToArray());
        return CompileParser(method, parameters);
    }

    private static ParseSpan<T> CompileParser(MethodInfo method, object[] parameters)
    {
        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        return Expression.Lambda<ParseSpan<T>>(Expression.Call(method, BindArguments(method, parameters, span)), span).Compile();
    }

    public Delegate? LocateTryParser(object[] parameters)
    {
        var parse = FindMethod(parameters.Select(x => x.GetType()).ToArray());
        return CompileTryParser(parse, parameters);
    }

    private static Delegate? CompileTryParser(MethodInfo parse, object[] parameters)
    {
        // Keep the selected format/options, preferring a span overload for each operation.
        var arguments = parse.GetParameters();
        var expected = parameters.Take(arguments.Length - 1).Select(x => x.GetType()).ToArray();
        var method = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(x => x.Name == (parse.Name == "ParseExact" ? "TryParseExact" : "TryParse") && x.ReturnType == typeof(bool))
            .Where(x => x.GetParameters() is var p && p.Length == arguments.Length + 1
                && p[^1].IsOut && p[^1].ParameterType == typeof(T).MakeByRefType()
                && Matches(p[..^1], expected))
            .OrderByDescending(x => x.GetParameters()[0].ParameterType == typeof(ReadOnlySpan<char>))
            .FirstOrDefault();
        if (method is null)
            return null;

        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var value = Expression.Parameter(typeof(T).MakeByRefType(), "value");
        var call = Expression.Call(method, BindArguments(method, parameters, span, hasOutput: true).Append(value));
        return Expression.Lambda<TryParseSpan<T>>(call, span, value).Compile();
    }

    public SpanParser.ParserEntry CreateEntry(Type type, object[] parameters)
    {
        // Snapshot the arguments and select Parse once; validation reuses this plan on first demand.
        var snapshot = parameters.ToArray();
        var method = FindMethod(snapshot.Select(x => x.GetType()).ToArray());
        var parse = CompileParser(method, snapshot);
        if (type == typeof(T))
            return new(parse, tryFactory: () => CompileTryParser(method, snapshot));
        if (Nullable.GetUnderlyingType(type) != typeof(T))
            throw new ArgumentException("The requested parser type must match the locator or its nullable type.", nameof(type));

        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var lifted = Expression.Lambda(typeof(ParseSpan<>).MakeGenericType(type),
            Expression.Convert(Expression.Invoke(Expression.Constant(parse), span), type), span).Compile();
        return new(lifted, tryFactory: () => LiftTryParser(type, CompileTryParser(method, snapshot)));
    }

    private static Delegate? LiftTryParser(Type type, Delegate? tryParse)
    {
        if (tryParse is null)
            return null;
        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var value = Expression.Parameter(type.MakeByRefType(), "value");
        var parsed = Expression.Variable(typeof(T), "parsed");
        var call = Expression.Invoke(Expression.Constant(tryParse), span, parsed);
        var body = Expression.Block([parsed], Expression.Condition(call,
            Expression.Block(Expression.Assign(value, Expression.Convert(parsed, type)), Expression.Constant(true)),
            Expression.Block(Expression.Assign(value, Expression.Default(type)), Expression.Constant(false))));
        return Expression.Lambda(typeof(TryParseSpan<>).MakeGenericType(type), body, span, value).Compile();
    }

    private static IEnumerable<Expression> BindArguments(MethodInfo method, object[] parameters, ParameterExpression span, bool hasOutput = false)
    {
        var arguments = method.GetParameters();
        yield return arguments[0].ParameterType == typeof(ReadOnlySpan<char>)
            ? span : Expression.Call(span, typeof(ReadOnlySpan<char>).GetMethod("ToString", Type.EmptyTypes)!);
        for (var i = 1; i < arguments.Length - (hasOutput ? 1 : 0); i++)
        {
            var type = arguments[i].ParameterType;
            yield return type == typeof(ReadOnlySpan<char>)
                ? Expression.Call(typeof(MemoryExtensions).GetMethod(nameof(MemoryExtensions.AsSpan), [typeof(string)])!,
                    Expression.Constant(parameters[i - 1], typeof(string)))
                : Expression.Constant(parameters[i - 1], type);
        }
    }

    private static bool Matches(ParameterInfo[] parameters, Type[] expected)
        => parameters.Length == expected.Length + 1
            && (parameters[0].ParameterType == typeof(ReadOnlySpan<char>) || parameters[0].ParameterType == typeof(string))
            && parameters.Skip(1).Select((parameter, index) => parameter.ParameterType.IsAssignableFrom(expected[index])
                || (parameter.ParameterType == typeof(ReadOnlySpan<char>) && expected[index] == typeof(string))).All(x => x);

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
            if (Matches(parameters, expected))
                matching.Add(method);
        }

        if (matching.Count == 0)
        {
            if (expected.Length > 0)
                return FindMethod(expected[0..(expected.Length-1)]);
            throw new InvalidOperationException($"No method found for type {Target.Name}");
        }

        return matching.OrderByDescending(x => x.GetParameters()[0].ParameterType == typeof(ReadOnlySpan<char>)).First();
    }

    Delegate ITypeParserLocator.LocateSpanParser(object[] parameters) => LocateSpanParser(parameters);
}
