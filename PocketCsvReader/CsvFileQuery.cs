using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly FileQuerySelection<TPartition> _selection;
    private readonly Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? _createReader;
    internal CsvProfile Profile { get; }

    internal CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        : this(profile, new FileQuerySelection<TPartition>(discover))
    {
    }

    internal CsvFileQuery(CsvProfile profile, AsyncFileDiscovery<TPartition> asyncSource)
        : this(profile, new FileQuerySelection<TPartition>(null, asyncSource))
    {
    }

    private CsvFileQuery(CsvProfile profile, FileQuerySelection<TPartition> selection,
        Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? createReader = null)
        => (Profile, _selection, _createReader) = (profile, selection, createReader);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
        => new(Profile, _selection.WithFilter(predicate), _createReader);

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(Profile, _selection.WithFilter(predicate, conditions), _createReader);

    internal CsvFileQuery<TPartition> WithReaderFactory(Func<PartitionedFile<TPartition>, Stream, CsvDataReader> createReader)
    {
        if (_createReader is not null)
            throw new InvalidOperationException("The query already has a custom reader factory.");
        return new(Profile, _selection, createReader);
    }

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles() => _selection.EnumerateFiles();

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public IAsyncEnumerable<PartitionedFile<TPartition>> EnumerateFilesAsync(CancellationToken cancellationToken = default)
        => _selection.EnumerateFilesAsync(cancellationToken);

    /// <summary>Creates a batch that starts opening matching files on the first read.</summary>
    public CsvBatchDataReader ToDataReader()
    {
        if (_selection.HasFilters && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            throw new InvalidOperationException("Partition filtering requires a header in each file when headers are enabled.");

        if (!_selection.RequiresAsync)
            return CsvBatchDataReader.FromDeferredInputs(EnumerateInputs(), Profile);

        var next = _selection.CreateAsyncDiscovery();
        return CsvBatchDataReader.FromAsyncInputs(NextInputAsync, Profile);

        async ValueTask<CsvBatchDataReader.ReaderInput?> NextInputAsync(CancellationToken token)
        {
            if (await next(token).ConfigureAwait(false) is not { } file)
                return null;
            var stream = await _selection.OpenAsync(file, token).ConfigureAwait(false);
            return CreateInput(file, stream);
        }
    }

    /// <summary>Creates a database reader that starts opening matching files on the first row request.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

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
        return CreateInput(file, stream);
    }

    private CsvBatchDataReader.ReaderInput CreateInput(PartitionedFile<TPartition> file, Stream stream)
        => new(stream, _createReader is null ? null : input => _createReader(file, input));
}
