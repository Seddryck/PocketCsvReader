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

    internal static DbDataReader Scan(IDataReader reader, IIndexKey key, object requested)
    {
        var scan = new ScanningDataReader(reader, row => Matches(row, key, requested));
        return reader is DbDataReader db
            ? new DbDataReaderAdapter(scan, db.NextResultAsync)
            : new DbDataReaderAdapter(scan);
    }

    internal static RecordSelection Select(NullableSpan field, IIndexKey key, object requested, bool ordered)
    {
        if (!field.HasValue)
            return RecordSelection.Skip;
        var comparison = key.Compare(key.Parse(field.Value), requested);
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

    private static bool Matches(IDataRecord row, IIndexKey key, object requested)
    {
        var ordinal = key.ColumnOrdinal ?? FindOrdinal(row, key.ColumnName!);
        if ((uint)ordinal >= (uint)row.FieldCount || row.IsDBNull(ordinal))
            return false;

        var field = row.GetValue(ordinal);
        if (field is null or DBNull)
            return false;
        if (field is string text && key.KeyType != typeof(string))
            field = key.Parse(text.AsSpan());
        if (!key.KeyType.IsInstanceOfType(field))
            throw new InvalidCastException($"The lookup column must contain '{key.KeyType}' values or text.");
        return key.Compare(field, requested) == 0;
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
