using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly CsvProfile _profile;
    private readonly IEnumerable<PartitionedFile<TPartition>> _files;

    internal CsvFileQuery(CsvProfile profile, IEnumerable<PartitionedFile<TPartition>> files)
        => (_profile, _files) = (profile, files);

    /// <summary>Discovers file descriptors without opening their input streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles()
    {
        foreach (var file in _files)
            yield return file ?? throw new ArgumentException("The file sequence contains a null descriptor.");
    }

    /// <summary>Creates a batch that starts opening files on the first read.</summary>
    public CsvBatchDataReader ToDataReader()
        => CsvBatchDataReader.FromDeferredStreamFactories(EnumerateOpeners(), _profile);

    /// <summary>Creates a database reader that starts opening files on the first row request.</summary>
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
}
