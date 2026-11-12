using System.Data;
using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Applies exact-key lookup to records exposed by an ADO.NET data reader.</summary>
public static class ReaderLookup
{
    /// <summary>Scans an existing reader using an optional index definition.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup<TKey>(this IDataReader reader, SparseIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        var requested = ValidateKey(index.Key, value);
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a format reader and path, or use an optional index.");
        return Scan(reader, index.Key, requested);
    }

    /// <summary>Scans an existing reader using an optional Bloom index definition.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup<TKey>(this IDataReader reader, BloomIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        var requested = ValidateKey(index.Key, value);
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a CSV reader and path, or use an optional index.");
        return Scan(reader, index.Key, requested);
    }

    /// <summary>Scans an existing reader for exact equality using an optional hash index.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup<TKey>(this IDataReader reader, HashIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        var requested = ValidateKey(index.Key, value);
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a CSV reader and path, or use an optional index.");
        return Scan(reader, index.Key, requested);
    }

    /// <summary>Scans an existing reader with the predicate of an optional bitmap index.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup(this IDataReader reader, BitmapIndex index, BitmapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a CSV reader and path, or use an optional index.");
        return Scan(reader, predicate.Bind(index.Keys).Matches);
    }

    /// <summary>Scans an existing reader for the terms of an optional inverted index.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup(this IDataReader reader, InvertedIndex index, TermPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a CSV reader and path, or use an optional index.");
        var lookup = predicate.Bind(index);
        return lookup.Terms.Count == 0 ? Scan(reader, static _ => false) : Scan(reader, index.Key, lookup.CreateMatcher());
    }

    /// <summary>Scans an existing reader for exact equality using an optional zone map.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup<TKey>(this IDataReader reader, ZoneMapIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        return Scan(reader, index, ZoneMapPredicate.EqualTo(index.Key, value));
    }

    /// <summary>Scans an existing reader for a typed range using an optional zone map.</summary>
    /// <remarks>The returned reader owns the input. Missing and null fields are skipped.</remarks>
    public static DbDataReader WithIndexedLookup<TKey>(this IDataReader reader, ZoneMapIndex index, IndexRange<TKey> range)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        return Scan(reader, index, ZoneMapPredicate.InRange(index.Key, range));
    }

    private static DbDataReader Scan(IDataReader reader, ZoneMapIndex index, ZoneMapPredicate predicate)
    {
        if (index.RequiresIndex)
            throw new NotSupportedException("An existing data reader cannot use a sidecar. Supply a CSV reader and path, or use an optional index.");
        return Scan(reader, index.Key, predicate.Contains);
    }

    internal static DbDataReader Scan(IDataReader reader, IIndexKey key, object requested)
        => Scan(reader, key, value => key.Compare(value, requested) == 0);

    private static DbDataReader Scan(IDataReader reader, IIndexKey key, Func<object, bool> matches)
        => Scan(reader, row => ReadKey(row, key) is { } field && matches(field));

    private static DbDataReader Scan(IDataReader reader, Func<IDataRecord, bool> matches)
    {
        var scan = new ScanningDataReader(reader, matches);
        return reader is DbDataReader db
            ? new DbDataReaderAdapter(scan, db.NextResultAsync)
            : new DbDataReaderAdapter(scan);
    }

    internal static RecordSelection Select(NullableSpan field, IIndexKey key, object requested, bool ordered)
    {
        if (!field.HasValue || key.Parse(field.Value) is not { } parsed)
            return RecordSelection.Skip;
        var comparison = key.Compare(parsed, requested);
        if (comparison == 0)
            return RecordSelection.Include;
        return comparison > 0 && ordered ? RecordSelection.Stop : RecordSelection.Skip;
    }

    internal static object ValidateKey<TKey>(IIndexKey key, TKey value)
    {
        if (typeof(TKey) != key.KeyType)
            throw new ArgumentException($"The lookup key type must be '{key.KeyType}'.", nameof(value));
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    internal static object? ReadKey(IDataRecord row, IIndexKey key)
    {
        var ordinal = key.ColumnOrdinal ?? FindOrdinal(row, key.ColumnName!);
        if ((uint)ordinal >= (uint)row.FieldCount || row.IsDBNull(ordinal))
            return null;

        var field = row.GetValue(ordinal);
        if (field is null or DBNull)
            return null;
        if (field is string text && key.KeyType != typeof(string))
            field = key.Parse(text.AsSpan());
        if (field is null)
            return null;
        if (!key.KeyType.IsInstanceOfType(field))
            throw new InvalidCastException($"The lookup column must contain '{key.KeyType}' values or text.");
        return field;
    }

    private static int FindOrdinal(IDataRecord row, string column)
    {
        // Some readers (including unprojected NDJSON) can reorder or omit fields on each row.
        for (int i = 0; i < row.FieldCount; i++)
        {
            if (string.Equals(row.GetName(i), column, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
