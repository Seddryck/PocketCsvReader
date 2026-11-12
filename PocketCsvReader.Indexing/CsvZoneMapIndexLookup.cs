using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Builds zone maps and applies typed equality and range lookups to CSV sources.</summary>
public static class CsvZoneMapIndexLookup
{
    /// <summary>Builds or replaces the zone map for one CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, ZoneMapIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() != typeof(CsvReader))
            throw UnsupportedSource();
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the zone map for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, ZoneMapIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!Supports(query))
            throw UnsupportedSource();

        var profile = query.Profile.ForIndexSeeking(trackPosition: true).WithFilters([]);
        var key = index.Key.ResolveRuntimeParser(query.Profile.Parsers);
        var compatibility = GetCompatibility(query.Profile, key);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw UnsupportedSource();
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
            throw UnsupportedSource();

        var key = predicate.Key.ResolveRuntimeParser(reader.Profile.Parsers);
        return new DbDataReaderAdapter(Select(reader.ToDataReader(path), predicate.WithKey(key)));
    }

    private static CsvFileQuery<TPartition> Lookup<TPartition>(CsvFileQuery<TPartition> query, ZoneMapIndex index,
        ZoneMapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(query);
        var profile = query.Profile;
        var key = predicate.Key.ResolveRuntimeParser(profile.Parsers);
        predicate = predicate.WithKey(key);
        var supported = Supports(query);
        if (!supported && index.RequiresIndex)
            throw UnsupportedSource();

        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, key));
        return query.WithReaderFactory((file, stream, currentProfile) =>
        {
            ZoneMapIndexSidecar.ReadPlan? plan = null;
            if (supported && file.Opener is null && stream is FileStream source
                && SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
                plan = ZoneMapIndexSidecar.Find(source, bomLength, index, compatibility, predicate);
            else if (index.RequiresIndex)
                throw UnsupportedSource();

            var readerProfile = plan is null ? currentProfile : currentProfile.ForIndexSeeking(trackPosition: false);
            return Select(new CsvDataReader(stream, readerProfile, leaveOpen: true), predicate, stream, plan);
        });
    }

    private static CsvDataReader Select(CsvDataReader reader, ZoneMapPredicate predicate, Stream? stream = null,
        ZoneMapIndexSidecar.ReadPlan? plan = null)
    {
        int ordinal = -1;
        if (plan is { } indexed)
            reader.SelectRecordBlocks(SelectRow, stream!, indexed.Blocks, indexed.RowCount, indexed.FieldCount);
        else
            reader.SelectRecords(SelectRow);
        return reader;

        RecordSelection SelectRow(CsvDataReader row)
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(row, predicate.Key);
            return predicate.Select(row.GetIndexField(ordinal));
        }
    }

    private static void BuildFile(string path, CsvProfile profile, ZoneMapIndex index, IIndexKey key, byte[] compatibility)
    {
        using var source = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        var stamp = SourceStamp.Read(source);
        if (!SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
            throw UnsupportedSource();

        using var reader = new CsvDataReader(source, profile, leaveOpen: true) { Window = null };
        var blocks = new List<ZoneMapIndexSidecar.Block>();
        int ordinal = -1, rowCount = 0;
        ZoneMapIndexSidecar.Block block = default;
        while (reader.Read())
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(reader, key);
            if (rowCount % index.BlockSize == 0)
            {
                if (reader.CurrentPosition is not { OffsetUnit: PositionOffsetUnit.Bytes } position)
                    throw new InvalidDataException("The reader did not provide a parser-confirmed byte position.");
                block = new(checked(position.Offset + bomLength), reader.RowCount - 1, null, null);
            }

            var field = reader.GetIndexField(ordinal);
            if (field.HasValue && key.Parse(field.Value) is { } value)
            {
                if (block.Min is null || key.Compare(value, block.Min) < 0)
                    block = block with { Min = value };
                if (block.Max is null || key.Compare(value, block.Max) > 0)
                    block = block with { Max = value };
            }

            rowCount = checked(rowCount + 1);
            if (rowCount % index.BlockSize == 0)
                blocks.Add(block);
        }

        if (rowCount % index.BlockSize != 0)
            blocks.Add(block);
        if (profile.Dialect.Header && ordinal < 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        ZoneMapIndexSidecar.Publish(source, stamp, index, compatibility, rowCount, reader.SourceHeaders?.Length ?? 0, blocks);
    }

    private static bool Supports<TPartition>(CsvFileQuery<TPartition> query)
        => !query.RequiresAsyncSource && CsvIndexLookup.Supports(query.Profile);

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            key.OrderingCompatibility, .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)
        ]));

    private static NotSupportedException UnsupportedSource()
        => new("Zone maps require filesystem CSV paths, uncompressed UTF-8, ASCII syntax, and per-file headers when enabled. Numbered comment rows, array fields, leading row counts, and bad-data policies are unsupported.");
}
