using System.Data.Common;
using System.Globalization;
using Clast.BloomFilter;

namespace PocketCsvReader.Indexing;

/// <summary>Builds and applies per-file Bloom filters for filesystem CSV queries.</summary>
public static class CsvBloomIndexLookup
{
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
        return new DbDataReaderAdapter(Select(reader.ToDataReader(path), key, requested));
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
        var requestedHash = new Lazy<ulong>(() => key.GetBloomHash(requested));
        return query.WithReaderFactory((_, stream, currentProfile) =>
            Select(new CsvDataReader(stream, currentProfile, leaveOpen: true), key, requested),
            supported ? file => PlanRead(file, index, compatibility, requestedHash) : null);
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
                filter.AddHash(key.GetBloomHash(value));
            rowCount = checked(rowCount + 1);
        }

        if (profile.Dialect.Header && ordinal < 0)
            CsvIndexLookup.ResolveOrdinal(reader, key);
        BloomIndexSidecar.Publish(stream, stamp, rowCount, compatibility, filter.Data);
    }

    private static CsvFileReadPlan PlanRead<TPartition>(PartitionedFile<TPartition> file, BloomIndex index,
        Lazy<byte[]> compatibility, Lazy<ulong> requestedHash)
    {
        if (file.Opener is not null)
        {
            if (index.RequiresIndex)
                throw UnsupportedSource();
            return default;
        }

        var probe = BloomIndexSidecar.Probe(file.Path, index, compatibility, requestedHash);
        if (probe is not { } result)
            return default;
        if (!result.MightContain)
            return new(SkippedRows: result.RowCount);

        return new(Validate: stream => ValidateOpenedSource(stream, result.Stamp));
    }

    private static void ValidateOpenedSource(Stream stream, SourceStamp stamp)
    {
        if (stream is not FileStream source || SourceStamp.Read(source) != stamp)
            throw new InvalidDataException("The CSV source changed after validating its Bloom index.");
    }

    private static CsvDataReader Select(CsvDataReader reader, IIndexKey key, object requested)
    {
        int ordinal = -1;
        reader.SelectRecords(row =>
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(row, key);
            return ReaderLookup.Select(row.GetIndexField(ordinal), key, requested, ordered: false);
        });
        return reader;
    }

    private static bool Supports<TPartition>(CsvFileQuery<TPartition> query)
        => !query.RequiresAsyncSource && CsvIndexLookup.Supports(query.Profile);

    private static byte[] GetCompatibility(CsvProfile profile, IIndexKey key)
    {
        var optimization = profile.ParserOptimizations;
        var schema = profile.Schema;
        return Convert.FromHexString(SparseIndexSidecar.Fingerprint([
            "parquet-split-block-le-v1", BloomKeyHash.Format, key.BloomHashCompatibility,
            key.ColumnName, key.ColumnOrdinal?.ToString(CultureInfo.InvariantCulture), key.CompatibilityId,
            CsvIndexLookup.GetConfiguration(profile).Id,
            profile.Dialect.NullSequence, profile.Dialect.MissingCell,
            profile.Resource?.Encoding, profile.StreamInitialization.ProbeEncoding.ToString(),
            profile.StreamInitialization.InputMode.ToString(), optimization.NoTextQualifier.ToString(),
            optimization.UnescapeChars.ToString(), optimization.HandleSpecialValues.ToString(),
            SequenceId(profile.Resource?.Sequences), schema?.IsMatchingByName.ToString(),
            schema is null ? null : SparseIndexSidecar.Fingerprint(schema.Fields.Select(field =>
                SparseIndexSidecar.Fingerprint([field.Name, SequenceId(field.Sequences)])))
        ]));
    }

    private static string? SequenceId(Configuration.ImmutableSequenceCollection? sequences)
        => sequences is null ? null : SparseIndexSidecar.Fingerprint(sequences
            .SelectMany(pair => new[] { pair.Key.ToString(), pair.Value?.ToString() }));

    private static NotSupportedException UnsupportedSource()
        => new("Bloom indexes require filesystem CSV paths, uncompressed UTF-8, ASCII syntax, and per-file headers when enabled. Numbered comment rows, array fields, leading row counts, and bad-data policies are unsupported.");
}
