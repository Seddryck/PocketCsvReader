using System.Globalization;

namespace PocketCsvReader.Indexing;

/// <summary>Builds sparse indexes for partitioned CSV queries and applies exact-key lookups.</summary>
public static class CsvIndexLookup
{
    /// <summary>Builds or replaces the sparse sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, SparseIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!Supports(query.Profile))
            throw UnsupportedSource();

        index = index.ResolveRuntimeParser(query.Profile.Parsers);
        var profile = CreateProfile(query.Profile, building: true);
        var configuration = GetConfiguration(profile);
        foreach (var file in query.EnumerateFiles())
        {
            if (file.Opener is not null)
                throw UnsupportedSource();

            int ordinal = -1;
            SparseIndexSidecar.Build(file.Path, profile.ParserOptimizations.BufferSize, index, configuration,
                (source, _) => new CsvDataReader(source, profile, leaveOpen: true),
                row =>
                {
                    if (ordinal < 0)
                        ordinal = ResolveOrdinal(row, index.Key);
                    return row.GetDecodedField(ordinal);
                },
                row =>
                {
                    if (profile.Dialect.Header && ordinal < 0)
                        ResolveOrdinal(row, index.Key);
                    return row.SourceHeaders?.Length ?? 0;
                });
        }
    }

    /// <summary>Seeks with a sparse sidecar. An optional index scans a file whose sidecar is missing.</summary>
    public static CsvFileQuery<TPartition> WithIndexedLookup<TPartition, TKey>(this CsvFileQuery<TPartition> query,
        SparseIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        var requested = ReaderLookup.ValidateKey(index.Key, value);
        var profile = query.Profile;
        index = index.ResolveRuntimeParser(profile.Parsers);
        var key = index.Key;
        var indexedProfile = Supports(profile) ? CreateProfile(profile, building: false) : null;
        var configuration = indexedProfile is null ? default : GetConfiguration(indexedProfile);
        if (index.RequiresIndex && indexedProfile is null)
            throw UnsupportedSource();

        return query.WithReaderFactory((descriptor, stream) =>
        {
            CsvRecordRange? range = null;
            if (descriptor.Opener is null && stream is FileStream source && indexedProfile is not null)
            {
                if (SparseIndexSidecar.Find(source, configuration, index, requested) is { } found)
                    range = new(found.Offset, found.SkippedRows, found.RowCount, found.FieldCount);
            }
            else if (index.RequiresIndex)
            {
                throw UnsupportedSource();
            }

            var reader = new CsvDataReader(stream, range is null ? profile : indexedProfile!, leaveOpen: true);
            int ordinal = -1;
            reader.SelectRecords(SelectRecord, stream, range);
            return reader;

            RecordSelection SelectRecord(CsvDataReader row)
            {
                if (ordinal < 0)
                    ordinal = ResolveOrdinal(row, key);
                return ReaderLookup.Select(row.GetDecodedField(ordinal), key, requested, ordered: range is not null);
            }
        });
    }

    internal static CsvFileQuery<bool> ForFile(CsvReader reader, string path)
    {
        var file = FileSource.Partitioned(path, true);
        return new(reader.Profile.ForSingleFile(), _ => [file]);
    }

    private static int ResolveOrdinal(CsvDataReader reader, IIndexKey key)
    {
        if (key.ColumnOrdinal is int ordinal)
            return checked(ordinal + (reader.HasRowNumber ? 1 : 0));
        var sourceHeaders = reader.SourceHeaders;
        var index = sourceHeaders is null ? -1 : Array.IndexOf(sourceHeaders, key.ColumnName);
        if (index < 0)
            throw new InvalidDataException($"Indexed column '{key.ColumnName}' was not found in the CSV header.");
        return index + (reader.HasRowNumber ? 1 : 0);
    }

    private static bool Supports(CsvProfile source)
        => Utf8RecordSource.Supports(source) && FlatFileIndexLookup.IsUncompressedUtf8(source)
            && source.Dialect.CommentRows is not { Length: > 0 } && !source.ParserOptimizations.RowCountAtStart
            && (!source.Dialect.Header || source.Dialect.HeaderRepeat);

    private static CsvProfile CreateProfile(CsvProfile source, bool building)
        => new(source.Dialect, building ? null : source.Schema, building ? null : source.Resource, building ? null : source.Parsers)
        {
            Filters = building ? [] : source.Filters,
            Skip = building ? 0 : source.Skip,
            Take = building ? null : source.Take,
            RowNumber = building ? null : source.RowNumber,
            StreamInitialization = new(ProbeEncoding: true, InputMode: CsvInputMode.Utf8Bytes),
            ParserOptimizations = source.ParserOptimizations with { TrackPosition = building }
        };

    private static SourceConfiguration GetConfiguration(CsvProfile profile)
    {
        var dialect = profile.Dialect;
        return new("csvConfigurationId", SparseIndexSidecar.Fingerprint([
            dialect.Header.ToString(), string.Join(",", dialect.HeaderRows.Select(row => row.ToString(CultureInfo.InvariantCulture))),
            dialect.HeaderJoin, dialect.HeaderRepeat.ToString(), dialect.CommentChar?.ToString(),
            dialect.Delimiter.ToString(), dialect.LineTerminator, dialect.QuoteChar?.ToString(),
            dialect.DoubleQuote.ToString(), dialect.EscapeChar?.ToString(), dialect.SkipInitialSpace.ToString()
        ]));
    }

    private static NotSupportedException UnsupportedSource()
        => new("Sparse indexes require filesystem CSV paths, uncompressed UTF-8, ASCII syntax, and per-file headers when enabled. Numbered comment rows, array fields, leading row counts, and bad-data policies are unsupported.");
}
