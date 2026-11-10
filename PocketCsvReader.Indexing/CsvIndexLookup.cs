namespace PocketCsvReader.Indexing;

/// <summary>Builds sparse indexes for partitioned CSV queries and applies exact-key lookups.</summary>
public static class CsvIndexLookup
{
    /// <summary>Builds or replaces the sparse sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, SparseIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!SparseIndexSidecar.Supports(query.Profile))
            throw UnsupportedSource();

        var profile = SparseIndexSidecar.CreateProfile(query.Profile, building: true);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw UnsupportedSource();
            SparseIndexSidecar.Build(file.Path, profile, index);
        }
    }

    /// <summary>Seeks with an available sparse sidecar and scans files that have no index.</summary>
    /// <remarks>Use <paramref name="requireIndex"/> to require a sidecar for every file. An invalid sidecar fails the lookup.</remarks>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        SparseIndex index, TKey value, bool requireIndex = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        var key = index.Key;
        var requested = ReaderLookup.ValidateKey(key, value);
        var profile = query.Profile;
        var indexedProfile = SparseIndexSidecar.Supports(profile)
            ? SparseIndexSidecar.CreateProfile(profile, building: false)
            : null;
        if (requireIndex && indexedProfile is null)
            throw UnsupportedSource();

        return query.WithReaderFactory((descriptor, stream) =>
        {
            CsvRecordRange? range = null;
            if (descriptor.Opener is null && stream is FileStream source && indexedProfile is not null)
            {
                try
                {
                    range = SparseIndexSidecar.Find(source, indexedProfile, index, requested);
                }
                catch (FileNotFoundException) when (!requireIndex)
                {
                    // No sidecar for this file. Scan from the current stream's beginning.
                }
            }
            else if (requireIndex)
            {
                throw UnsupportedSource();
            }

            var reader = new CsvDataReader(stream, range is null ? profile : indexedProfile!, leaveOpen: true);
            int ordinal = -1;
            reader.SelectRecords(SelectRecord, stream, range);
            return reader;

            CsvRecordDecision SelectRecord(CsvDataReader row)
            {
                if (ordinal < 0)
                    ordinal = ResolveOrdinal(row, key);
                var field = row.GetDecodedField(ordinal);
                if (!field.HasValue)
                    return CsvRecordDecision.Skip;

                var comparison = key.Compare(key.Parse(field.Value), requested);
                if (comparison == 0)
                    return CsvRecordDecision.Include;
                return comparison > 0 && range is not null ? CsvRecordDecision.Stop : CsvRecordDecision.Skip;
            }
        });
    }

    internal static int ResolveOrdinal(CsvDataReader reader, IIndexKey key)
    {
        if (key.ColumnOrdinal is int ordinal)
            return checked(ordinal + (reader.HasRowNumber ? 1 : 0));
        var sourceHeaders = reader.SourceHeaders;
        var index = sourceHeaders is null ? -1 : Array.IndexOf(sourceHeaders, key.ColumnName);
        if (index < 0)
            throw new InvalidDataException($"Indexed column '{key.ColumnName}' was not found in the CSV header.");
        return index + (reader.HasRowNumber ? 1 : 0);
    }

    private static NotSupportedException UnsupportedSource()
        => new("Sparse indexes require filesystem CSV paths, uncompressed UTF-8, ASCII syntax, and per-file headers when enabled. Numbered comment rows, array fields, leading row counts, and bad-data policies are unsupported.");
}
