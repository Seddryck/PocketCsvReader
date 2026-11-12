namespace PocketCsvReader.Indexing;

internal delegate RecordSelection SelectIndexField(NullableSpan field);

internal readonly record struct CsvIndexedReadPlan(IReadOnlyList<CsvRecordBlock> Blocks, int RowCount, int FieldCount)
{
    internal static void AddCandidate(List<CsvRecordBlock> candidates, CsvRecordBlock block)
    {
        // Keep the same parser and its read-ahead when consecutive records qualify.
        if (candidates.Count > 0 && candidates[^1] is var previous
            && previous.SkippedDataRows + previous.RowCount == block.SkippedDataRows)
            candidates[^1] = previous with { RowCount = previous.RowCount + block.RowCount };
        else
            candidates.Add(block);
    }
}

internal static class CsvSeekIndexLookup
{
    internal static bool Supports<TPartition>(CsvFileQuery<TPartition> query)
        => !query.RequiresAsyncSource && CsvIndexLookup.Supports(query.Profile);

    internal static CsvFileQuery<TPartition> WithLookup<TPartition>(CsvFileQuery<TPartition> query, IIndexKey key,
        SelectIndexField select, Func<FileStream, int, CsvIndexedReadPlan?> find, bool required, string indexName)
        => WithLookup(query, _ => CreateSelector(key, select), find, required, indexName);

    internal static CsvFileQuery<TPartition> WithLookup<TPartition>(CsvFileQuery<TPartition> query,
        Func<bool, Func<CsvDataReader, RecordSelection>> createSelector,
        Func<FileStream, int, CsvIndexedReadPlan?> find, bool required, string indexName)
    {
        var supported = Supports(query);
        if (!supported && required)
            throw UnsupportedSource(indexName);

        return query.WithReaderFactory((file, stream, profile) =>
        {
            CsvIndexedReadPlan? plan = null;
            if (supported && file.Opener is null && stream is FileStream { CanSeek: true } source
                && SparseIndexSidecar.TryReadUtf8BomLength(source, out var bomLength))
                plan = find(source, bomLength);
            else if (required)
                throw UnsupportedSource(indexName);

            var readerProfile = plan is null ? profile : profile.ForIndexSeeking(trackPosition: false);
            return Select(new CsvDataReader(stream, readerProfile, leaveOpen: true), createSelector(plan.HasValue), stream, plan);
        });
    }

    internal static CsvDataReader Select(CsvDataReader reader, IIndexKey key, SelectIndexField select)
        => Select(reader, CreateSelector(key, select));

    internal static CsvDataReader Select(CsvDataReader reader, Func<CsvDataReader, RecordSelection> select, Stream? stream = null,
        CsvIndexedReadPlan? plan = null)
    {
        if (plan is { } indexed)
        {
            ArgumentNullException.ThrowIfNull(stream);
            reader.SelectRecordBlocks(select, stream, indexed.Blocks, indexed.RowCount, indexed.FieldCount);
        }
        else
        {
            reader.SelectRecords(select);
        }
        return reader;
    }

    private static Func<CsvDataReader, RecordSelection> CreateSelector(IIndexKey key, SelectIndexField select)
    {
        int ordinal = -1;
        return row =>
        {
            if (ordinal < 0)
                ordinal = CsvIndexLookup.ResolveOrdinal(row, key);
            return select(row.GetIndexField(ordinal));
        };
    }

    internal static long GetRecordOffset(CsvDataReader reader, int bomLength)
        => reader.CurrentPosition is { OffsetUnit: PositionOffsetUnit.Bytes } position
            ? checked(position.Offset + bomLength)
            : throw new InvalidDataException("The reader did not provide a parser-confirmed byte position.");

    internal static NotSupportedException UnsupportedSource(string indexName)
        => new($"{indexName} require filesystem CSV paths, uncompressed UTF-8, ASCII syntax, and per-file headers when enabled. Numbered comment rows, array fields, leading row counts, and bad-data policies are unsupported.");
}
