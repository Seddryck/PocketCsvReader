using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Builds zone maps and applies typed equality and range lookups to CSV sources.</summary>
public static class CsvZoneMapIndexLookup
{
    private const string IndexName = "Zone maps";

    /// <summary>Builds or replaces the zone map for one CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, ZoneMapIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() != typeof(CsvReader))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the zone map for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, ZoneMapIndex index)
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
            BuildFile(file.Path, profile, index, key, compatibility);
        }
    }

    /// <summary>Finds exact matches in one CSV file, skipping disjoint blocks.</summary>
    public static DbDataReader WithIndexedLookup<TKey>(this CsvReader reader, string path, ZoneMapIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(index);
        return Lookup(reader, path, index, ZoneMapPredicate.EqualTo(index.Key, value));
    }

    /// <summary>Finds records within a typed range in one CSV file, skipping disjoint blocks.</summary>
    public static DbDataReader WithIndexedLookup<TKey>(this CsvReader reader, string path, ZoneMapIndex index, IndexRange<TKey> range)
    {
        ArgumentNullException.ThrowIfNull(index);
        return Lookup(reader, path, index, ZoneMapPredicate.InRange(index.Key, range));
    }

    /// <summary>Finds exact matches in the selected CSV files, skipping disjoint blocks.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        ZoneMapIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(index);
        return Lookup(query, index, ZoneMapPredicate.EqualTo(index.Key, value));
    }

    /// <summary>Finds records within a typed range in the selected CSV files, skipping disjoint blocks.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        ZoneMapIndex index, IndexRange<TKey> range)
    {
        ArgumentNullException.ThrowIfNull(index);
        return Lookup(query, index, ZoneMapPredicate.InRange(index.Key, range));
    }

    private static DbDataReader Lookup(CsvReader reader, string path, ZoneMapIndex index, ZoneMapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (reader.GetType() == typeof(CsvReader))
            return Lookup(CsvIndexLookup.ForFile(reader, path), index, predicate).ToDbDataReader();
        if (index.RequiresIndex)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var key = predicate.Key.ResolveRuntimeParser(reader.Profile.Parsers);
        return new DbDataReaderAdapter(CsvSeekIndexLookup.Select(reader.ToDataReader(path), key, predicate.WithKey(key).Select));
    }

    private static CsvFileQuery<TPartition> Lookup<TPartition>(CsvFileQuery<TPartition> query, ZoneMapIndex index,
        ZoneMapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(query);
        var profile = query.Profile;
        var key = predicate.Key.ResolveRuntimeParser(profile.Parsers);
        predicate = predicate.WithKey(key);
        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, key));
        return CsvSeekIndexLookup.WithLookup(query, CsvIndexKind.ZoneMap, key, predicate.Select,
            (source, bomLength) => ZoneMapIndexSidecar.Find(source, bomLength, index, compatibility, predicate),
            index.RequiresIndex, IndexName);
    }

    private static void BuildFile(string path, CsvProfile profile, ZoneMapIndex index, IIndexKey key, byte[] compatibility)
    {
        using var source = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        var stamp = SourceStamp.Read(source);
        if (!SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        using var reader = new CsvDataReader(source, profile, leaveOpen: true) { Window = null };
        var (blocks, rowCount) = BuildBlocks(reader, index.BlockSize, bomLength, key);
        if (profile.Dialect.Header && rowCount == 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        ZoneMapIndexSidecar.Publish(source, stamp, index, compatibility, rowCount, reader.SourceHeaders?.Length ?? 0, blocks);
    }

    private static (List<ZoneMapIndexSidecar.Block> Blocks, int RowCount) BuildBlocks(CsvDataReader reader,
        int blockSize, int bomLength, IIndexKey key)
    {
        var blocks = new List<ZoneMapIndexSidecar.Block>();
        int ordinal = -1, rowCount = 0;
        ZoneMapIndexSidecar.Block block = default;
        while (reader.Read())
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(reader, key);
            if (rowCount % blockSize == 0)
                block = new(CsvSeekIndexLookup.GetRecordOffset(reader, bomLength), reader.RowCount - 1, null, null);

            block = ExtendBounds(block, reader.GetIndexField(ordinal), key);
            rowCount = checked(rowCount + 1);
            if (rowCount % blockSize == 0)
                blocks.Add(block);
        }

        if (rowCount % blockSize != 0)
            blocks.Add(block);
        return (blocks, rowCount);
    }

    private static ZoneMapIndexSidecar.Block ExtendBounds(ZoneMapIndexSidecar.Block block, NullableSpan field, IIndexKey key)
    {
        if (!field.HasValue || key.Parse(field.Value) is not { } value)
            return block;
        if (block.Min is null || key.Compare(value, block.Min) < 0)
            block = block with { Min = value };
        if (block.Max is null || key.Compare(value, block.Max) > 0)
            block = block with { Max = value };
        return block;
    }

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            key.OrderingCompatibility, .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)
        ]));
}
