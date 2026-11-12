using System.Data.Common;

namespace PocketCsvReader.Indexing;

/// <summary>Builds bitmap indexes and applies categorical predicates to CSV sources.</summary>
public static class CsvBitmapIndexLookup
{
    private const string IndexName = "Bitmap indexes";

    /// <summary>Builds or replaces the bitmap sidecar for one CSV file.</summary>
    public static void BuildIndexes(this CsvReader reader, string path, BitmapIndex index)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        if (reader.GetType() != typeof(CsvReader))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        CsvIndexLookup.ForFile(reader, path).BuildIndexes(index);
    }

    /// <summary>Builds or replaces the bitmap sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, BitmapIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!CsvSeekIndexLookup.Supports(query))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var profile = query.Profile.ForIndexSeeking(trackPosition: true).WithFilters([]);
        var keys = index.ResolveKeys(query.Profile.Parsers);
        var compatibility = GetCompatibility(query.Profile, keys);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
            BuildFile(file.Path, profile, keys, compatibility);
        }
    }

    /// <summary>Finds records matching a bitmap predicate in one CSV file.</summary>
    public static DbDataReader WithIndexedLookup(this CsvReader reader, string path, BitmapIndex index, BitmapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        if (reader.GetType() == typeof(CsvReader))
            return CsvIndexLookup.ForFile(reader, path).WithIndexedLookup(index, predicate).ToDbDataReader();
        if (index.RequiresIndex)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        var lookup = predicate.Bind(index.ResolveKeys(reader.Profile.Parsers));
        return new DbDataReaderAdapter(CsvSeekIndexLookup.Select(reader.ToDataReader(path), lookup.CreateCsvSelector()));
    }

    /// <summary>Finds records matching a bitmap predicate in the selected CSV files.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition>(this CsvFileQuery<TPartition> query,
        BitmapIndex index, BitmapPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(predicate);
        var profile = query.Profile;
        var keys = index.ResolveKeys(profile.Parsers);
        var lookup = predicate.Bind(keys);
        var compatibility = new Lazy<byte[]>(() => GetCompatibility(profile, keys));
        return CsvSeekIndexLookup.WithLookup(query, CsvIndexKind.Bitmap,
            indexed => indexed ? null : lookup.CreateCsvSelector(),
            (source, bomLength) => BitmapIndexSidecar.Find(source, bomLength, index.RequiresIndex, keys, lookup, compatibility),
            index.RequiresIndex, IndexName);
    }

    private static void BuildFile(string path, CsvProfile profile, IIndexKey[] keys, byte[] compatibility)
    {
        using var source = SparseIndexSidecar.OpenSource(path, profile.ParserOptimizations.BufferSize, building: true);
        if (!source.CanSeek)
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);
        var stamp = SourceStamp.Read(source);
        if (!SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
            throw CsvSeekIndexLookup.UnsupportedSource(IndexName);

        using var reader = new CsvDataReader(source, profile, leaveOpen: true) { Window = null };
        var columns = keys.Select(key => new BitmapColumn(key)).ToArray();
        var locations = ReadLocations(reader, keys, columns, bomLength);
        if (profile.Dialect.Header && locations.Count == 0)
        {
            foreach (var key in keys)
                CsvIndexLookup.ResolveOrdinal(reader, key);
        }
        BitmapIndexSidecar.Publish(source, stamp, compatibility, reader.SourceHeaders?.Length ?? 0, locations, columns);
    }

    private static List<BitmapIndexSidecar.RowLocation> ReadLocations(CsvDataReader reader, IIndexKey[] keys,
        BitmapColumn[] columns, int bomLength)
    {
        var locations = new List<BitmapIndexSidecar.RowLocation>();
        int[]? ordinals = null;
        while (reader.Read())
        {
            ordinals ??= keys.Select(key => CsvIndexLookup.ResolveOrdinal(reader, key)).ToArray();
            for (int column = 0; column < columns.Length; column++)
                columns[column].Add(reader.GetIndexField(ordinals[column]), locations.Count);
            locations.Add(new(CsvSeekIndexLookup.GetRecordOffset(reader, bomLength), reader.RowCount - 1));
        }
        return locations;
    }

    private static byte[] GetCompatibility(CsvProfile profile, IReadOnlyList<IIndexKey> keys)
        => Convert.FromHexString(SparseIndexSidecar.Fingerprint(keys.Select(key =>
            SparseIndexSidecar.Fingerprint([key.OrderingCompatibility, .. CsvIndexLookup.GetKeyCompatibilitySettings(profile, key)]))));
}
