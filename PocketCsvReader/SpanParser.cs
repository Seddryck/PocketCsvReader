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

/// <summary>Parses a span synchronously without creating an intermediate string.</summary>
/// <remarks>The parser must consume the span during the call and must not retain it.</remarks>
public delegate T ParseSpan<T>(ReadOnlySpan<char> span);
internal delegate bool TryParseSpan<T>(ReadOnlySpan<char> span, out T value);
internal class SpanParser
{
    internal class ParserEntry(Delegate typed, ParseSpan<object>? boxed = null, Delegate? tryParse = null, Func<Delegate?>? tryFactory = null)
    {
        public Delegate Typed { get; } = typed;
        private ParseSpan<object>? _boxed = boxed;
        private Delegate? _tryParse = tryParse;
        private Func<Delegate?>? _tryFactory = tryFactory;

        internal TryParseSpan<T> GetTryParser<T>()
        {
            if (Typed is not ParseSpan<T> parse)
                throw new InvalidOperationException($"Parser is not a ParseSpan<{typeof(T).Name}>.");
            if (_tryParse is null)
            {
                var resolved = _tryFactory is null ? FindTryParser(parse) : _tryFactory();
                if (resolved is not null && resolved is not TryParseSpan<T>)
                    throw new InvalidOperationException($"Validation parser is not a TryParseSpan<{typeof(T).Name}>.");
                _tryParse = resolved ?? CreateFallback(parse);
                // The cached delegate (including a Parse-only fallback) replaces the resolution plan.
                _tryFactory = null;
            }
            return (TryParseSpan<T>)_tryParse;
        }

        private static TryParseSpan<T>? FindTryParser<T>(ParseSpan<T> parse)
        {
            // A direct static Parse method can opt into its matching TryParse without
            // replacing custom lambdas or parsers that capture their own behavior.
            var method = parse.Method;
            if (!method.IsStatic || method.Name != "Parse" || method.DeclaringType is null
                || method.GetParameters() is not [{ ParameterType: var input }] || input != typeof(ReadOnlySpan<char>))
                return null;
            var candidate = method.DeclaringType.GetMethod("TryParse",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, [typeof(ReadOnlySpan<char>), typeof(T).MakeByRefType()], null);
            return candidate is { ReturnType: var result } && result == typeof(bool) && candidate.GetParameters()[1].IsOut
                ? candidate.CreateDelegate<TryParseSpan<T>>() : null;
        }

        private static TryParseSpan<T> CreateFallback<T>(ParseSpan<T> parse)
            => (ReadOnlySpan<char> span, out T value) =>
            {
                try
                {
                    value = parse(span);
                    return true;
                }
                catch (Exception ex) when (IsConversionFailure(ex))
                {
                    value = default!;
                    return false;
                }
            };

        // Runtime registrations compile the adapter only when object access is first needed.
        public ParseSpan<object> Boxed => _boxed ??= Typed as ParseSpan<object> ?? CreateBoxed();

        protected virtual ParseSpan<object> CreateBoxed() => BoxParser(Typed);
    }

    private sealed class TypedParserEntry<T>(ParseSpan<T> parse, TryParseSpan<T>? tryParse)
        : ParserEntry(parse, tryParse: tryParse)
    {
        protected override ParseSpan<object> CreateBoxed() => span => ((ParseSpan<T>)Typed)(span)!;
    }

    private Dictionary<Type, ParserEntry> TypeParsers { get; } = new();
    private Dictionary<int, ParserEntry> FieldParsers { get; } = new();

    public void ClearFieldParsers() => FieldParsers.Clear();

    internal void BindFieldParser(int index, ParserEntry parser) => FieldParsers[index] = parser;

    internal static Delegate Adapt(Type type, Configuration.ParseFunction parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var span = Expression.Parameter(typeof(ReadOnlySpan<char>), "span");
        var text = Expression.Call(span, typeof(ReadOnlySpan<char>).GetMethod("ToString", Type.EmptyTypes)!);
        var result = Expression.Convert(Expression.Invoke(Expression.Constant(parse), text), type);
        return Expression.Lambda(typeof(ParseSpan<>).MakeGenericType(type), result, span).Compile();
    }

    public SpanParser()
    {
        Register<string>(s => s.ToString(), (ReadOnlySpan<char> s, out string value) => { value = s.ToString(); return true; });
        Register<char>(s => s[0], (ReadOnlySpan<char> s, out char value) => { value = s.IsEmpty ? default : s[0]; return !s.IsEmpty; });
        Register<Guid>(Guid.Parse, Guid.TryParse);
        Register<bool>(bool.Parse, bool.TryParse);
        RegisterInvariant<byte>();
        RegisterInvariant<short>();
        RegisterInvariant<int>();
        RegisterInvariant<long>();
        RegisterInvariant<decimal>();
        RegisterInvariant<float>();
        RegisterInvariant<double>();
        RegisterInvariant<DateTime>();
        RegisterInvariant<DateOnly>();
        RegisterInvariant<TimeOnly>();
        RegisterInvariant<DateTimeOffset>();
    }

    private void RegisterInvariant<T>() where T : ISpanParsable<T>
        => Register<T>(s => T.Parse(s, CultureInfo.InvariantCulture),
            (ReadOnlySpan<char> s, out T value) => T.TryParse(s, CultureInfo.InvariantCulture, out value!));

    public void Register<T>(ParseSpan<T> parse, TryParseSpan<T>? tryParse = null)
    {
        ArgumentNullException.ThrowIfNull(parse);
        TypeParsers[typeof(T)] = new TypedParserEntry<T>(parse, tryParse);
    }

    public void Register(Type type, Delegate dlg, Delegate? tryParse = null)
        => TypeParsers[type] = CreateEntry(type, dlg, tryParse);

    internal static ParserEntry CreateEntry(Type type, Delegate dlg, Delegate? tryParse = null)
    {
        ArgumentNullException.ThrowIfNull(dlg);
        if (!IsParseSpan(dlg, out var parseType) || type != parseType)
            throw new ArgumentException("Unexpected delegate.", nameof(dlg));
        if (tryParse is not null && tryParse.GetType() != typeof(TryParseSpan<>).MakeGenericType(type))
            throw new ArgumentException("Unexpected TryParse delegate.", nameof(tryParse));
        return new(dlg, tryParse: tryParse);
    }

    internal static ParserEntry CreateEntry<T>(ParseSpan<T> parse)
        => new TypedParserEntry<T>(parse, null);

    public void Register<T>(int index, ParseSpan<T> parse, TryParseSpan<T>? tryParse = null)
    {
        ArgumentNullException.ThrowIfNull(parse);
        FieldParsers[index] = new TypedParserEntry<T>(parse, tryParse);
    }

    public void Register(int index, Type type, Delegate dlg, Delegate? tryParse = null)
        => FieldParsers[index] = CreateEntry(type, dlg, tryParse);

    public void CacheTypeParser<T>(int index)
    {
        if (!TypeParsers.TryGetValue(typeof(T), out var parser))
            throw new InvalidOperationException($"No parser registered for type {typeof(T).Name}");
        FieldParsers[index] = parser;
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
        if (TypeParsers.TryGetValue(typeof(T), out var entry))
        {
            var dlg = entry.Typed;
            if (dlg is not ParseSpan<T>)
                throw new InvalidOperationException($"Parser for type {typeof(T).Name} is not a ParseSpan<{typeof(T).Name}> but a {dlg.GetType().Name}");
            parse = (ParseSpan<T>)dlg!;
            return true;
        }
        parse = null;
        return false;
    }

    public bool TryGetParser(Type type, [NotNullWhen(true)] out ParseSpan<object>? parse)
    {
        if (TypeParsers.TryGetValue(type, out var entry))
        {
            parse = entry.Boxed;
            return true;
        }
        parse = null;
        return false;
    }

    public bool TryGetParser<T>(int index, [NotNullWhen(true)] out ParseSpan<T>? parse)
    {
        if (FieldParsers.TryGetValue(index, out var entry) && entry.Typed is ParseSpan<T> typed)
        {
            parse = typed;
            return true;
        }
        parse = null;
        return false;
    }

    public ParseSpan<T> GetParser<T>(int index)
    {
        if (FieldParsers.TryGetValue(index, out var entry))
        {
            var dlg = entry.Typed;
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
        if (FieldParsers.TryGetValue(index, out var entry))
        {
            parse = entry.Boxed;
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

    internal bool TryGetTryParser<T>(int index, [NotNullWhen(true)] out TryParseSpan<T>? parse)
    {
        if (FieldParsers.TryGetValue(index, out var entry) && entry.Typed is ParseSpan<T>)
        {
            parse = entry.GetTryParser<T>();
            return true;
        }
        parse = null;
        return false;
    }

    internal bool TryGetTryParser<T>([NotNullWhen(true)] out TryParseSpan<T>? parse)
    {
        if (TypeParsers.TryGetValue(typeof(T), out var entry))
        {
            parse = entry.GetTryParser<T>();
            return true;
        }
        parse = null;
        return false;
    }

    /// <summary>Tries the registered field parser; missing parsers and conversion failures return false.</summary>
    /// <remarks>Does not fall back to a type parser. Unexpected parser and configuration errors propagate.</remarks>
    public bool TryParse<T>(int index, ReadOnlySpan<char> span, out T? value)
    {
        if (TryGetTryParser<T>(index, out var parse) && parse(span, out value))
            return true;
        value = default;
        return false;
    }

    /// <summary>Tries the registered type parser; missing parsers and conversion failures return false.</summary>
    public bool TryParse<T>(ReadOnlySpan<char> span, out T? value)
    {
        if (TryGetTryParser<T>(out var parse) && parse(span, out value))
            return true;
        value = default;
        return false;
    }

    private static bool IsConversionFailure(Exception exception)
        => exception is FormatException or OverflowException or InvalidCastException
            || (exception is System.Reflection.TargetInvocationException { InnerException: { } inner } && IsConversionFailure(inner));

    private static readonly Lazy<SpanParser> _defaultParser = new(() => new());

    public static SpanParser Default
        => _defaultParser.Value;
}
