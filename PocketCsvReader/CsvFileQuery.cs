using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly CsvProfile _profile;
    private readonly IEnumerable<PartitionedFile<TPartition>> _files;
    private readonly FileFilter[] _filters;

    internal CsvFileQuery(CsvProfile profile, IEnumerable<PartitionedFile<TPartition>> files)
        : this(profile, files, [])
    {
    }

    private CsvFileQuery(CsvProfile profile, IEnumerable<PartitionedFile<TPartition>> files, FileFilter[] filters)
        => (_profile, _files, _filters) = (profile, files, filters);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(_profile, _files, [.. _filters, new(predicate, null)]);
    }

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(_profile, _files, [.. _filters, new(predicate, conditions)]);

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles()
    {
        foreach (var file in _files)
        {
            if (file is null)
                throw new ArgumentException("The file sequence contains a null descriptor.");
            if (_filters.All(filter => filter.Match(file.Partition)))
                yield return file;
        }
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
