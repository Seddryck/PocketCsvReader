using System.Data;

namespace PocketCsvReader.Indexing;

/// <summary>Defines typed equality and membership tests for columns in a bitmap index.</summary>
public sealed class BitmapPredicate
{
    private readonly Clause[] _clauses;

    private BitmapPredicate(Clause[] clauses) => _clauses = clauses;

    /// <summary>Matches a single value in the named column.</summary>
    public static BitmapPredicate Equal<TKey>(string columnName, TKey value) => In(columnName, value);

    /// <summary>Matches a single value at the zero-based source ordinal.</summary>
    public static BitmapPredicate Equal<TKey>(int columnOrdinal, TKey value) => In(columnOrdinal, value);

    /// <summary>Matches any of the supplied values in the named column.</summary>
    public static BitmapPredicate In<TKey>(string columnName, params TKey[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return Create(columnName, null, values);
    }

    /// <summary>Matches any of the supplied values at the zero-based source ordinal.</summary>
    public static BitmapPredicate In<TKey>(int columnOrdinal, params TKey[] values)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnOrdinal);
        return Create(null, columnOrdinal, values);
    }

    /// <summary>Requires both predicates to match the same data row.</summary>
    public BitmapPredicate And(BitmapPredicate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new([.. _clauses, .. other._clauses]);
    }

    internal BitmapLookup Bind(IReadOnlyList<IIndexKey> keys)
    {
        var values = new SortedSet<object>?[keys.Count];
        foreach (var clause in _clauses)
        {
            int column = FindColumn(keys, clause);
            var key = keys[column];
            if (key.KeyType != clause.KeyType)
                throw new ArgumentException($"The bitmap column '{key.ColumnName ?? key.ColumnOrdinal.ToString()}' requires '{key.KeyType}' values.");

            if (values[column] is { } existing)
                existing.IntersectWith(clause.Values);
            else
                values[column] = new(clause.Values, Comparer<object>.Create(key.Compare));
        }
        return new(keys, values);
    }

    private static BitmapPredicate Create<TKey>(string? name, int? ordinal, TKey[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var snapshot = new object[values.Length];
        for (int i = 0; i < values.Length; i++)
            snapshot[i] = values[i] ?? throw new ArgumentException("Bitmap predicate values must be non-null.", nameof(values));
        return new([new(name, ordinal, typeof(TKey), snapshot)]);
    }

    private static int FindColumn(IReadOnlyList<IIndexKey> keys, Clause clause)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].ColumnName == clause.Name && keys[i].ColumnOrdinal == clause.Ordinal)
                return i;
        }
        throw new ArgumentException($"Column '{clause.Name ?? clause.Ordinal.ToString()}' is not defined in the bitmap index.");
    }

    private sealed record Clause(string? Name, int? Ordinal, Type KeyType, object[] Values);
}

internal sealed class BitmapLookup
{
    private readonly Condition[] _conditions;

    internal BitmapLookup(IReadOnlyList<IIndexKey> keys, SortedSet<object>?[] values)
    {
        var conditions = new List<Condition>();
        for (int column = 0; column < keys.Count; column++)
        {
            if (values[column] is { } requested)
                conditions.Add(new(keys[column], requested));
        }
        _conditions = conditions.ToArray();
        Values = values;
    }

    internal IReadOnlyList<SortedSet<object>?> Values { get; }

    internal Func<CsvDataReader, RecordSelection> CreateCsvSelector()
    {
        if (_conditions.Any(condition => condition.Values.Count == 0))
            return static _ => RecordSelection.Skip;

        int[]? ordinals = null;
        return row =>
        {
            ordinals ??= _conditions.Select(condition => CsvIndexLookup.ResolveOrdinal(row, condition.Key)).ToArray();
            for (int i = 0; i < _conditions.Length; i++)
            {
                var condition = _conditions[i];
                var field = row.GetIndexField(ordinals[i]);
                if (!field.HasValue || condition.Key.Parse(field.Value) is not { } parsed || !condition.Values.Contains(parsed))
                    return RecordSelection.Skip;
            }
            return RecordSelection.Include;
        };
    }

    internal bool Matches(IDataRecord row)
    {
        foreach (var condition in _conditions)
        {
            if (condition.Values.Count == 0 || ReaderLookup.ReadKey(row, condition.Key) is not { } parsed
                || !condition.Values.Contains(parsed))
                return false;
        }
        return true;
    }

    private readonly record struct Condition(IIndexKey Key, SortedSet<object> Values);
}
