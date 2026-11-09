using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly CsvProfile _profile;
    private readonly Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> _discover;
    private readonly FileFilter[] _filters;

    internal CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        : this(profile, discover, [])
    {
    }

    private CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover, FileFilter[] filters)
        => (_profile, _discover, _filters) = (profile, discover, filters);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(_profile, _discover, [.. _filters, new(predicate, null)]);
    }

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(_profile, _discover, [.. _filters, new(predicate, conditions)]);

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
        if (_filters.Length != 0 && _profile.Dialect.Header && !_profile.Dialect.HeaderRepeat)
            throw new InvalidOperationException("Partition filtering requires a header in each file when headers are enabled.");

        return CsvBatchDataReader.FromDeferredStreamFactories(EnumerateOpeners(), _profile);
    }

    /// <summary>Creates a database reader that starts opening matching files on the first row request.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

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

    private sealed record FileFilter(Func<TPartition, bool> Match, PartitionCondition[]? Conditions);
}
