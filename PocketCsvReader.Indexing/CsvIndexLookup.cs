using System.Globalization;

namespace PocketCsvReader.Indexing;

/// <summary>Builds sparse indexes for partitioned CSV queries and applies exact-key lookups.</summary>
public static class CsvIndexLookup
{
    private const string IndexName = "Sparse indexes";

    /// <summary>Builds or replaces the sparse sidecar for each selected CSV file.</summary>
    public static void BuildIndexes<TPartition>(this CsvFileQuery<TPartition> query, SparseIndex index)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(index);
        if (!Supports(query.Profile))
            throw UnsupportedSource();

        index = index.ResolveRuntimeParser(query.Profile.Parsers);
        var profile = CreateBuildProfile(query.Profile);
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
        var configuration = GetConfiguration(profile);
        return CsvSeekIndexLookup.WithLookup(query, CsvIndexKind.Sparse, CreateSelector, (source, _) =>
            SparseIndexSidecar.Find(source, configuration, index, requested) is { } found ? ToReadPlan(found) : null,
            index.RequiresIndex, IndexName);

        Func<CsvDataReader, RecordSelection> CreateSelector(bool indexed)
        {
            int ordinal = -1;
            return row =>
            {
                if (ordinal < 0)
                    ordinal = ResolveOrdinal(row, key);
                return ReaderLookup.Select(row.GetDecodedField(ordinal), key, requested, ordered: indexed);
            };
        }
    }

    private static CsvIndexedReadPlan ToReadPlan(SparseRecordRange range)
    {
        IReadOnlyList<CsvRecordBlock>? blocks = null;
        if (range.Offset is long offset)
            blocks = [new(offset, range.SkippedRows, range.SkippedRows, range.RowCount - range.SkippedRows)];
        return new(blocks, range.RowCount, range.FieldCount);
    }

    internal static CsvFileQuery<bool> ForFile(CsvReader reader, string path)
    {
        var file = FileSource.Partitioned(path, true);
        return new(reader.Profile.ForSingleFile(), _ => [file]);
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

    internal static bool Supports(CsvProfile source)
        => Utf8RecordSource.Supports(source) && FlatFileIndexLookup.IsUncompressedUtf8(source)
            && source.Dialect.CommentRows is not { Length: > 0 } && !source.ParserOptimizations.RowCountAtStart
            && (!source.Dialect.Header || source.Dialect.HeaderRepeat);

    private static CsvProfile CreateBuildProfile(CsvProfile source)
        => new(source.Dialect)
        {
            StreamInitialization = new(ProbeEncoding: true, InputMode: CsvInputMode.Utf8Bytes),
            ParserOptimizations = source.ParserOptimizations with { TrackPosition = true }
        };

    internal static SourceConfiguration GetConfiguration(CsvProfile profile)
    {
        var dialect = profile.Dialect;
        return new("csvConfigurationId", SparseIndexSidecar.Fingerprint([
            dialect.Header.ToString(), string.Join(",", dialect.HeaderRows.Select(row => row.ToString(CultureInfo.InvariantCulture))),
            dialect.HeaderJoin, dialect.HeaderRepeat.ToString(), dialect.CommentChar?.ToString(),
            dialect.Delimiter.ToString(), dialect.LineTerminator, dialect.QuoteChar?.ToString(),
            dialect.DoubleQuote.ToString(), dialect.EscapeChar?.ToString(), dialect.SkipInitialSpace.ToString()
        ]));
    }

    internal static string?[] GetKeyCompatibilitySettings(CsvProfile profile, IIndexKey key)
    {
        var optimization = profile.ParserOptimizations;
        var schema = profile.Schema;
        return [
            key.ColumnName, key.ColumnOrdinal?.ToString(CultureInfo.InvariantCulture), key.CompatibilityId,
            GetConfiguration(profile).Id, profile.Dialect.NullSequence, profile.Dialect.MissingCell,
            profile.Resource?.Encoding, profile.StreamInitialization.ProbeEncoding.ToString(),
            profile.StreamInitialization.InputMode.ToString(), optimization.NoTextQualifier.ToString(),
            optimization.UnescapeChars.ToString(), optimization.HandleSpecialValues.ToString(),
            SequenceId(profile.Resource?.Sequences), schema?.IsMatchingByName.ToString(),
            schema is null ? null : SparseIndexSidecar.Fingerprint(schema.Fields.Select(field =>
                SparseIndexSidecar.Fingerprint([field.Name, SequenceId(field.Sequences)])))
        ];
    }

    private static string? SequenceId(Configuration.ImmutableSequenceCollection? sequences)
        => sequences is null ? null : SparseIndexSidecar.Fingerprint(sequences
            .SelectMany(pair => new[] { pair.Key.ToString(), pair.Value?.ToString() }));

    private static NotSupportedException UnsupportedSource()
        => CsvSeekIndexLookup.UnsupportedSource(IndexName);
}
