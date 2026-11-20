using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PocketCsvReader;

/// <summary>Maps named CSV columns to writable properties of a target object.</summary>
/// <typeparam name="T">The target type.</typeparam>
public sealed class ObjectMap<T>
{
    private readonly Func<CsvDataReader, Func<T>> _bindTarget;
    private readonly List<(string Header, Func<T, CsvDataReader, int, T> Assign)> _properties = [];

    private ObjectMap(Func<CsvDataReader, Func<T>> bindTarget)
        => _bindTarget = bindTarget;

    /// <summary>Creates a map that calls a factory for each row.</summary>
    public static ObjectMap<T> Create(Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ObjectMap<T>(_ => factory);
    }

    /// <summary>Creates a map that resolves each target from a named key column.</summary>
    /// <remarks>The key is parsed with the reader's typed field access before the resolver is called.</remarks>
    public static ObjectMap<T> Create<TKey>(string keyHeader, Func<TKey, T> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHeader);
        ArgumentNullException.ThrowIfNull(resolve);

        return new ObjectMap<T>(reader =>
        {
            var ordinal = ResolveOrdinal(reader.Fields!, keyHeader);
            return () => resolve(reader.GetFieldValue<TKey>(ordinal));
        });
    }

    /// <summary>Maps a CSV header to a public, settable instance property on the target.</summary>
    public ObjectMap<T> Map<TProperty>(string header, Expression<Func<T, TProperty>> property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(property);

        var target = property.Parameters[0];
        if (property.Body is not MemberExpression { Member: PropertyInfo member } access
            || access.Expression != target || member.GetSetMethod() is not { } setter
            || setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
        {
            throw new ArgumentException("Select a public, settable instance property directly on the target.", nameof(property));
        }

        var value = Expression.Parameter(typeof(TProperty), "value");
        // Return the assigned target so value-type targets retain their property changes.
        var assign = Expression.Lambda<Func<T, TProperty, T>>(
            Expression.Block(Expression.Assign(access, value), target), target, value).Compile();
        _properties.Add((header, (row, reader, ordinal) => assign(row, reader.GetFieldValue<TProperty>(ordinal))));
        return this;
    }

    internal IEnumerable<T> Read(CsvDataReader reader)
    {
        var hasRow = reader.Read();
        if (reader.Fields is not { } headers)
            yield break;

        var resolve = _bindTarget(reader);
        var bindings = _properties.Select(p => (Ordinal: ResolveOrdinal(headers, p.Header), p.Assign)).ToArray();

        while (hasRow)
        {
            var target = resolve();
            foreach (var (ordinal, assign) in bindings)
                target = assign(target, reader, ordinal);
            yield return target;
            hasRow = reader.Read();
        }
    }

    private static int ResolveOrdinal(string[] headers, string header)
    {
        var ordinal = Array.IndexOf(headers, header);
        if (ordinal < 0)
            throw new InvalidDataException($"CSV header '{header}' was not found.");
        if (Array.IndexOf(headers, header, ordinal + 1) >= 0)
            throw new InvalidDataException($"CSV header '{header}' is ambiguous because it occurs more than once.");
        return ordinal;
    }
}
