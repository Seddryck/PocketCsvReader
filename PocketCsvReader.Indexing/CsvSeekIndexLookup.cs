namespace PocketCsvReader.Indexing;

internal delegate RecordSelection SelectIndexField(NullableSpan field);

internal readonly record struct CsvIndexedReadPlan(IReadOnlyList<CsvRecordBlock>? Blocks, int RowCount, int FieldCount)
{
    internal CsvIndexedReadPlan Intersect(CsvIndexedReadPlan other)
    {
        if (RowCount != other.RowCount || FieldCount != other.FieldCount)
            throw new InvalidDataException("CSV indexes disagree on the source row or field count.");
        if (Blocks is null)
            return other;
        if (other.Blocks is null)
            return this;
        return this with { Blocks = IntersectBlocks(Blocks, other.Blocks) };
    }

    private static List<CsvRecordBlock> IntersectBlocks(IReadOnlyList<CsvRecordBlock> left, IReadOnlyList<CsvRecordBlock> right)
    {
        var result = new List<CsvRecordBlock>();
        int i = 0, j = 0;
        while (i < left.Count && j < right.Count)
        {
            var first = left[i];
            var second = right[j];
            int firstEnd = first.SkippedDataRows + first.RowCount;
            int secondEnd = second.SkippedDataRows + second.RowCount;
            var start = LaterStart(first, second);
            int end = Math.Min(firstEnd, secondEnd);
            if (start.SkippedDataRows < end)
                AddCandidate(result, start with { RowCount = end - start.SkippedDataRows });
            if (firstEnd <= secondEnd)
                i++;
            if (secondEnd <= firstEnd)
                j++;
        }
        return result;
    }

    private static CsvRecordBlock LaterStart(CsvRecordBlock first, CsvRecordBlock second)
    {
        if (first.SkippedDataRows != second.SkippedDataRows)
            return first.SkippedDataRows > second.SkippedDataRows ? first : second;
        if (first.Offset != second.Offset)
            throw new InvalidDataException("CSV indexes disagree on a record offset.");
        // Sparse sidecars store data-row counts at checkpoints. Other sidecars
        // also account for comment records in the physical row count.
        return first.SkippedRows >= second.SkippedRows ? first : second;
    }

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

    internal static CsvFileQuery<TPartition> WithLookup<TPartition>(CsvFileQuery<TPartition> query, CsvIndexKind kind, IIndexKey key,
        SelectIndexField select, Func<FileStream, int, CsvIndexedReadPlan?> find, bool required, string indexName)
        => WithLookup(query, kind, _ => CreateSelector(key, select), find, required, indexName);

    internal static CsvFileQuery<TPartition> WithLookup<TPartition>(CsvFileQuery<TPartition> query, CsvIndexKind kind,
        Func<bool, Func<CsvDataReader, RecordSelection>?> createSelector,
        Func<FileStream, int, CsvIndexedReadPlan?> find, bool required, string indexName)
    {
        var supported = Supports(query);
        if (!supported && required)
            throw UnsupportedSource(indexName);

        return CsvIndexPipeline<TPartition>.Add(query,
            new(kind, createSelector, required ? indexName : null, Find: supported ? find : null));
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

    internal static Func<CsvDataReader, RecordSelection> CreateSelector(IIndexKey key, SelectIndexField select)
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
