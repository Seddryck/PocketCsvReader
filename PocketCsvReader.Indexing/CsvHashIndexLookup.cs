using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Builds and applies direct hash lookups for CSV sources.</summary>
public static class CsvHashIndexLookup
{
    private const string IndexName = "Hash indexes";

    /// <summary>Builds or replaces the hash sidecar for one CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, HashIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() != typeof(CsvReader))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the hash sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, HashIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!CsvSeekIndexLookup.Supports(query))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var profile = query.Profile.ForIndexSeeking(trackPosition: true).WithFilters([]);
        var key = index.Key.ResolveRuntimeParser(query.Profile.Parsers);
        var compatibility = GetCompatibility(query.Profile, key);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
            BuildFile(file.Path, profile, key, compatibility);
        }
    }

    /// <summary>Finds exact matches in one CSV file by seeking to candidate records.</summary>
    public static DbDataReader WithIndexedLookup<TKey>(this CsvReader reader, string path, HashIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() == typeof(CsvReader))
            return CsvIndexLookup.ForFile(reader, path).WithIndexedLookup(index, value).ToDbDataReader();
        if (index.RequiresIndex)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var key = index.Key.ResolveRuntimeParser(reader.Profile.Parsers);
        var requested = ReaderLookup.ValidateKey(key, value);
        return new DbDataReaderAdapter(CsvSeekIndexLookup.Select(reader.ToDataReader(path), key,
            field => ReaderLookup.Select(field, key, requested, ordered: false)));
    }

    /// <summary>Finds exact matches in the selected CSV files by seeking to candidate records.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        HashIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        var profile = query.Profile;
        var key = index.Key.ResolveRuntimeParser(profile.Parsers);
        var requested = ReaderLookup.ValidateKey(key, value);
        // Optional scans need neither a persistent parser identity nor a hash.
        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, key));
        var hash = new Lazy<ulong>(() => key.GetStableHash(requested));
        return CsvSeekIndexLookup.WithLookup(query, key, field => ReaderLookup.Select(field, key, requested, ordered: false),
            (source, bomLength) => HashIndexSidecar.Find(source, bomLength, index, compatibility, hash),
            index.RequiresIndex, IndexName);
    }

    private static void BuildFile(string path, CsvProfile profile, IIndexKey key, byte[] compatibility)
    {
        using var source = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        if (!source.CanSeek)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        var stamp = SourceStamp.Read(source);
        if (!SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        using var reader = new CsvDataReader(source, profile, leaveOpen: true) { Window = null };
        var entries = new List<HashIndexSidecar.Entry>();
        int ordinal = -1, rowCount = 0;
        while (reader.Read())
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(reader, key);
            var field = reader.GetIndexField(ordinal);
            if (field.HasValue && key.Parse(field.Value) is { } value)
            {
                entries.Add(new(key.GetStableHash(value), CsvSeekIndexLookup.GetRecordOffset(reader, bomLength),
                    reader.RowCount - 1, rowCount));
            }
            rowCount = checked(rowCount + 1);
        }

        if (profile.Dialect.Header && ordinal < 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        HashIndexSidecar.Publish(source, stamp, compatibility, rowCount, reader.SourceHeaders?.Length ?? 0, entries);
    }

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            StableIndexKeyHash.Format, key.HashCompatibility, .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)
        ]));
}
