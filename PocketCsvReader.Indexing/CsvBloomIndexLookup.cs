using System.Data.Common;
using Clast.BloomFilter;

namespace PocketCsvReader.Indexing;

/// <summary>Builds and applies per-file Bloom filters for filesystem CSV queries.</summary>
public static class CsvBloomIndexLookup
{
    private const string IndexName = "Bloom indexes";

    /// <summary>Builds or replaces the sidecar for a single CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, BloomIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (reader.GetType() != typeof(CsvReader))
            throw UnsupportedSource();
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the Bloom sidecar for every selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, BloomIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!Supports(query))
            throw UnsupportedSource();

        var profile = query.Profile.WithFilters([]);
        var key = index.Key.ResolveRuntimeParser(profile.Parsers);
        var compatibility = GetCompatibility(profile, key);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw UnsupportedSource();

            BuildFile(file.Path, profile, key, index.PayloadBytes, compatibility);
        }
    }

    /// <summary>Applies exact equality to one CSV file, skipping it if the sidecar rules it out.</summary>
    public static DbDataReader WithIndexedLookup<TKey>(this CsvReader reader, string path, BloomIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() == typeof(CsvReader))
            return CsvIndexLookup.ForFile(reader, path).WithIndexedLookup(index, value).ToDbDataReader();
        if (index.RequiresIndex)
            throw UnsupportedSource();

        var key = index.Key.ResolveRuntimeParser(reader.Profile.Parsers);
        var requested = ReaderLookup.ValidateKey(key, value);
        return new DbDataReaderAdapter(CsvSeekIndexLookup.Select(reader.ToDataReader(path), key,
            field => ReaderLookup.Select(field, key, requested, ordered: false)));
    }

    /// <summary>Prunes files using their sidecars, then applies exact typed equality to the remaining files.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        BloomIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        var profile = query.Profile;
        var key = index.Key.ResolveRuntimeParser(profile.Parsers);
        var requested = ReaderLookup.ValidateKey(key, value);
        var supported = Supports(query);
        if (!supported && index.RequiresIndex)
            throw UnsupportedSource();

        // Missing sidecars may use even a stateful parser in optional scan mode.
        // Only require a persistable parser identity when consulting a sidecar.
        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, key));
        var requestedHash = new Lazy<ulong>(() => key.GetStableHash(requested));
        return CsvIndexPipeline<TPartition>.Add(query, new(CsvIndexKind.Bloom,
            _ => CsvSeekIndexLookup.CreateSelector(key, field => ReaderLookup.Select(field, key, requested, ordered: false)),
            index.RequiresIndex ? IndexName : null,
            Probe: supported ? path => BloomIndexSidecar.Probe(path, index, compatibility, requestedHash) : null));
    }

    private static void BuildFile(string path, CsvProfile profile, IIndexKey key, int payloadBytes, byte[] compatibility)
    {
        var filter = new SplitBlockBloomFilterBuilder(payloadBytes);
        using var stream = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        var stamp = SourceStamp.Read(stream);
        using var reader = new CsvDataReader(stream, profile, leaveOpen: true) { Window = null };
        int ordinal = -1, rowCount = 0;
        while (reader.Read())
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(reader, key);
            var field = reader.GetIndexField(ordinal);
            if (field.HasValue && key.Parse(field.Value) is { } value)
                filter.AddHash(key.GetStableHash(value));
            rowCount = checked(rowCount + 1);
        }

        if (profile.Dialect.Header && ordinal < 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        BloomIndexSidecar.Publish(stream, stamp, rowCount, compatibility, filter.Data);
    }

    private static bool Supports<TPartition>(CsvFileQuery<TPartition> query)
        => !query.RequiresAsyncSource && CsvIndexLookup.Supports(query.Profile);

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            "parquet-split-block-le-v1", StableIndexKeyHash.Format, key.HashCompatibility,
            .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)
        ]));

    private static NotSupportedException UnsupportedSource()
        => CsvSeekIndexLookup.UnsupportedSource(IndexName);
}
