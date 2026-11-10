using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly CsvProfile _profile;
    private readonly Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> _discover;
    private readonly FileFilter[] _filters;
    private readonly IndexedLookup? _indexedLookup;

    internal CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        : this(profile, discover, [])
    {
    }

    private CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover, FileFilter[] filters,
        IndexedLookup? indexedLookup = null)
        => (_profile, _discover, _filters, _indexedLookup) = (profile, discover, filters, indexedLookup);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(_profile, _discover, [.. _filters, new(predicate, null)], _indexedLookup);
    }

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(_profile, _discover, [.. _filters, new(predicate, conditions)], _indexedLookup);

    /// <summary>Builds or replaces the sparse sidecar for each selected CSV file.</summary>
    public void BuildIndexes(SparseIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        RequirePerFileHeaders("Sparse indexing");
        var profile = SparseIndexSidecar.CreateProfile(_profile, building: true);
        foreach (var file in EnumerateFiles())
            SparseIndexSidecar.Build(GetIndexSourcePath(file), profile, index);
    }

    /// <summary>Uses the sparse index to find records whose key compares equal to the supplied value.</summary>
    public CsvFileQuery<TPartition> WithIndexedLookup<TKey>(SparseIndex index, TKey value)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (typeof(TKey) != index.Key.KeyType)
            throw new ArgumentException($"The lookup key type must be '{index.Key.KeyType}'.", nameof(value));
        ArgumentNullException.ThrowIfNull(value);
        if (_indexedLookup is not null)
            throw new InvalidOperationException("A file query supports one indexed lookup.");
        return new(_profile, _discover, _filters, new(index, value));
    }

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles()
    {
        // Conditions after a delegate cannot move ahead of it. The delegate may throw or reject the file.
        var pruningConditions = _filters.TakeWhile(filter => filter.Conditions is not null)
            .SelectMany(filter => filter.Conditions ?? []).ToArray();
        return _discover(pruningConditions).Select(RequireDescriptor)
            .Where(file => _filters.All(filter => filter.Match(file.Partition)));
    }

    /// <summary>Creates a batch that starts opening matching files on the first read.</summary>
    public CsvBatchDataReader ToDataReader()
    {
        if (_indexedLookup is not null)
            RequirePerFileHeaders("Sparse indexing");
        else if (_filters.Length != 0)
            RequirePerFileHeaders("Partition filtering");

        if (_indexedLookup is { } lookup)
        {
            var profile = SparseIndexSidecar.CreateProfile(_profile, building: false);
            return CsvBatchDataReader.FromDeferredStreamFactories(EnumerateIndexOpeners(), profile, stream =>
            {
                var file = (FileStream)stream;
                var selection = SparseIndexSidecar.Find(file, profile, lookup.Index, lookup.Value);
                var reader = new CsvDataReader(file, profile, leaveOpen: true);
                reader.ConfigureIndexedLookup(file, selection);
                return reader;
            });
        }

        return CsvBatchDataReader.FromDeferredStreamFactories(EnumerateOpeners(), _profile);
    }

    /// <summary>Creates a database reader that starts opening matching files on the first row request.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

    private void RequirePerFileHeaders(string operation)
    {
        if (_profile.Dialect.Header && !_profile.Dialect.HeaderRepeat)
            throw new InvalidOperationException($"{operation} requires a header in each file when headers are enabled.");
    }

    private static PartitionedFile<TPartition> RequireDescriptor(PartitionedFile<TPartition>? file)
        => file ?? throw new ArgumentException("The file sequence contains a null descriptor.");

    private IEnumerable<Func<Stream>> EnumerateOpeners()
    {
        foreach (var file in EnumerateFiles())
        {
            yield return file.Opener ?? (() => new FileStream(
                file.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                _profile.ParserOptimizations.BufferSize, useAsync: true));
        }
    }

    private IEnumerable<Func<Stream>> EnumerateIndexOpeners()
    {
        foreach (var file in EnumerateFiles())
            yield return () => SparseIndexSidecar.OpenSource(GetIndexSourcePath(file), _profile.ParserOptimizations.BufferSize);
    }

    private static string GetIndexSourcePath(PartitionedFile<TPartition> file)
        => file.Opener is null ? file.Path : throw new NotSupportedException("Sparse indexes require filesystem CSV paths without custom stream openers.");

    private sealed record FileFilter(Func<TPartition, bool> Match, PartitionCondition[]? Conditions);
    private sealed record IndexedLookup(SparseIndex Index, object Value);
}
