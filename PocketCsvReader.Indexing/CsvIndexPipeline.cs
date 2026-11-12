namespace PocketCsvReader.Indexing;

internal enum CsvIndexKind
{
    Bloom,
    ZoneMap,
    Bitmap,
    Inverted,
    Hash,
    Sparse
}

internal sealed record CsvIndexStage(CsvIndexKind Kind, Func<bool, Func<CsvDataReader, RecordSelection>?> CreateSelector,
    string? RequiredIndex, Func<FileStream, int, CsvIndexedReadPlan?>? Find = null,
    Func<string, BloomIndexSidecar.ProbeResult?>? Probe = null);

internal sealed class CsvIndexPipeline<TPartition> : ICsvFileReaderFactory<TPartition>
{
    private readonly CsvIndexStage[] _stages;

    private CsvIndexPipeline(IEnumerable<CsvIndexStage> stages) => _stages = stages.OrderBy(stage => stage.Kind).ToArray();

    internal static CsvFileQuery<TPartition> Add(CsvFileQuery<TPartition> query, CsvIndexStage stage)
    {
        IEnumerable<CsvIndexStage> stages = query.ReaderFactory switch
        {
            null => [stage],
            CsvIndexPipeline<TPartition> pipeline => pipeline._stages.Append(stage),
            _ => throw new InvalidOperationException("The query already has a custom reader factory.")
        };
        return query.WithReaderFactory(new CsvIndexPipeline<TPartition>(stages));
    }

    CsvFileReadPlan ICsvFileReaderFactory<TPartition>.PlanRead(PartitionedFile<TPartition> file)
    {
        SourceStamp? stamp = null;
        foreach (var stage in _stages)
        {
            if (stage.Kind != CsvIndexKind.Bloom)
                break;
            if (Probe(stage, file) is not { } result)
                continue;
            if (stamp is { } previous && previous != result.Stamp)
                throw new InvalidDataException("The CSV source changed while validating its Bloom indexes.");
            if (!result.MightContain)
                return new(SkippedRows: result.RowCount);
            stamp = result.Stamp;
        }

        return stamp is { } expected ? new(Validate: stream => ValidateBloomSource(stream, expected)) : default;
    }

    CsvDataReader ICsvFileReaderFactory<TPartition>.CreateReader(PartitionedFile<TPartition> file, Stream stream, CsvProfile profile)
    {
        var source = file.Opener is null && stream is FileStream { CanSeek: true } local ? local : null;
        var (plan, select) = PlanRecords(source);
        var readerProfile = plan.HasValue ? profile.ForIndexSeeking(trackPosition: false) : profile;
        var reader = new CsvDataReader(stream, readerProfile, leaveOpen: true);
        return CsvSeekIndexLookup.Select(reader, select, stream, plan);
    }

    private (CsvIndexedReadPlan? Plan, Func<CsvDataReader, RecordSelection> Select) PlanRecords(FileStream? source)
    {
        int? bomLength = null;
        CsvIndexedReadPlan? plan = null;
        var selectors = new List<(CsvIndexKind Kind, Func<CsvDataReader, RecordSelection> Select)>();
        foreach (var stage in _stages)
        {
            var found = Find(stage, source, ref bomLength);
            if (found is { } candidates)
            {
                plan = plan is { } current ? current.Intersect(candidates) : candidates;
                if (plan.Value.Blocks is { Count: 0 })
                    return (plan, static _ => RecordSelection.Skip);
            }
            if (stage.CreateSelector(found.HasValue) is { } select)
                selectors.Add((stage.Kind, select));
        }

        return (plan, CombineSelectors(selectors));
    }

    private static Func<CsvDataReader, RecordSelection> CombineSelectors(
        List<(CsvIndexKind Kind, Func<CsvDataReader, RecordSelection> Select)> selectors)
    {
        if (selectors.Count == 0)
            return static _ => RecordSelection.Include;
        if (selectors.Count == 1)
            return selectors[0].Select;

        var ordered = selectors.OrderBy(selector => PredicateOrder(selector.Kind)).Select(selector => selector.Select).ToArray();
        return row =>
        {
            foreach (var select in ordered)
            {
                var result = select(row);
                if (result != RecordSelection.Include)
                    return result;
            }
            return RecordSelection.Include;
        };
    }

    private static int PredicateOrder(CsvIndexKind kind) => kind switch
    {
        CsvIndexKind.Sparse => -1,
        CsvIndexKind.Bloom => int.MaxValue,
        _ => (int)kind
    };

    private static BloomIndexSidecar.ProbeResult? Probe(CsvIndexStage stage, PartitionedFile<TPartition> file)
    {
        if (stage.Probe is not { } probe)
            return null;
        if (file.Opener is null)
            return probe(file.Path);
        if (stage.RequiredIndex is { } name)
            throw CsvSeekIndexLookup.UnsupportedSource(name);
        return null;
    }

    private static CsvIndexedReadPlan? Find(CsvIndexStage stage, FileStream? source, ref int? bomLength)
    {
        if (stage.Find is not { } find)
            return null;
        if (source is not null)
        {
            bomLength ??= SparseIndexSidecar.TryReadUtf8BomLength(source, out var length) ? length : -1;
            if (bomLength is int bytes && bytes >= 0)
                return find(source, bytes);
        }
        if (stage.RequiredIndex is { } name)
            throw CsvSeekIndexLookup.UnsupportedSource(name);
        return null;
    }

    private static void ValidateBloomSource(Stream stream, SourceStamp stamp)
    {
        if (stream is not FileStream source || SourceStamp.Read(source) != stamp)
            throw new InvalidDataException("The CSV source changed after validating its Bloom index.");
    }
}
