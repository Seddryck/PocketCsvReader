using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> _discover;
    private readonly FileFilter[] _filters;
    private readonly Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? _createReader;
    internal CsvProfile Profile { get; }

    internal CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        : this(profile, discover, [])
    {
    }

    private CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover, FileFilter[] filters,
        Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? createReader = null)
        => (Profile, _discover, _filters, _createReader) = (profile, discover, filters, createReader);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(Profile, _discover, [.. _filters, new(predicate, null)], _createReader);
    }

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(Profile, _discover, [.. _filters, new(predicate, conditions)], _createReader);

    internal CsvFileQuery<TPartition> WithReaderFactory(Func<PartitionedFile<TPartition>, Stream, CsvDataReader> createReader)
    {
        if (_createReader is not null)
            throw new InvalidOperationException("The query already has a custom reader factory.");
        return new(Profile, _discover, _filters, createReader);
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
        if (_filters.Length != 0 && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            throw new InvalidOperationException("Partition filtering requires a header in each file when headers are enabled.");

        return CsvBatchDataReader.FromDeferredInputs(EnumerateInputs(), Profile);
    }

    /// <summary>Creates a database reader that starts opening matching files on the first row request.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

    private static PartitionedFile<TPartition> RequireDescriptor(PartitionedFile<TPartition>? file)
        => file ?? throw new ArgumentException("The file sequence contains a null descriptor.");

    private IEnumerable<Func<CsvBatchDataReader.ReaderInput>> EnumerateInputs()
    {
        foreach (var file in EnumerateFiles())
            yield return () => OpenInput(file);
    }

    private CsvBatchDataReader.ReaderInput OpenInput(PartitionedFile<TPartition> file)
    {
        var stream = file.Opener is { } open
            ? open()
            : new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                Profile.ParserOptimizations.BufferSize, useAsync: true);
        return new(stream, _createReader is null ? null : input => _createReader(file, input));
    }

    private sealed record FileFilter(Func<TPartition, bool> Match, PartitionCondition[]? Conditions);
}
