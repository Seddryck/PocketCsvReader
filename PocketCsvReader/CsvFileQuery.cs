using System.Data.Common;

namespace PocketCsvReader;

// A skipped input contributes source row numbers but owns no stream.
internal readonly record struct CsvFileReadPlan(int? SkippedRows = null, Action<Stream>? Validate = null);

internal interface ICsvFileReaderFactory<TPartition>
{
    CsvFileReadPlan PlanRead(PartitionedFile<TPartition> file);
    CsvDataReader CreateReader(PartitionedFile<TPartition> file, Stream stream, CsvProfile profile);
}

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly FileQuerySelection<TPartition> _selection;
    internal ICsvFileReaderFactory<TPartition>? ReaderFactory { get; }
    internal CsvProfile Profile { get; }
    internal bool RequiresAsyncSource => _selection.RequiresAsync;

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
        ICsvFileReaderFactory<TPartition>? readerFactory = null)
        => (Profile, _selection, ReaderFactory) = (profile, selection, readerFactory);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
        => new(Profile, _selection.WithFilter(predicate), ReaderFactory);

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(Profile, _selection.WithFilter(predicate, conditions), ReaderFactory);

    /// <summary>Keeps rows whose sanitized cell equals the expected text. Null never matches.</summary>
    public CsvFileQuery<TPartition> WithFilter(string column, string expected, StringComparison comparison = StringComparison.Ordinal)
        => WithFilter(column, ReaderFilter.TextEquals(expected, comparison));

    /// <summary>Keeps rows whose sanitized cell at the given ordinal equals the expected text.</summary>
    public CsvFileQuery<TPartition> WithFilter(int ordinal, string expected, StringComparison comparison = StringComparison.Ordinal)
        => WithFilter(ordinal, ReaderFilter.TextEquals(expected, comparison));

    /// <summary>Keeps rows accepted by a sanitized-span predicate. The span is valid only during the call.</summary>
    public CsvFileQuery<TPartition> WithFilter(string column, ParseSpan<bool> predicate)
        => AddFilter(ReaderFilter.ForCell(column, predicate));

    /// <summary>Keeps rows accepted by a sanitized-span predicate at the given ordinal.</summary>
    public CsvFileQuery<TPartition> WithFilter(int ordinal, ParseSpan<bool> predicate)
        => AddFilter(ReaderFilter.ForCell(ordinal, predicate));

    /// <summary>Keeps rows accepted by a typed predicate using the getter's parsing rules.</summary>
    public CsvFileQuery<TPartition> WithFilter<T>(string column, Func<T, bool> predicate)
        => AddFilter(ReaderFilter.ForCell(column, predicate));

    /// <summary>Keeps rows accepted by a typed predicate at the given ordinal.</summary>
    public CsvFileQuery<TPartition> WithFilter<T>(int ordinal, Func<T, bool> predicate)
        => AddFilter(ReaderFilter.ForCell(ordinal, predicate));

    private CsvFileQuery<TPartition> AddFilter(ReaderFilter filter)
        => new(Profile.WithFilters(Array.AsReadOnly<ReaderFilter>([.. Profile.Filters, filter])),
            _selection, ReaderFactory);

    internal CsvFileQuery<TPartition> WithReaderFactory(ICsvFileReaderFactory<TPartition> factory)
        => new(Profile, _selection, factory);

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
        var plan = ReaderFactory?.PlanRead(file) ?? default;
        if (plan.SkippedRows is int skipped)
            return new(null, SkippedRows: skipped);

        var stream = file.Opener is { } open
            ? open()
            : new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                Profile.ParserOptimizations.BufferSize, useAsync: true);
        try
        {
            plan.Validate?.Invoke(stream);
            return CreateInput(file, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private CsvBatchDataReader.ReaderInput CreateInput(PartitionedFile<TPartition> file, Stream stream)
        => new(stream, ReaderFactory is { } factory ? input => factory.CreateReader(file, input, Profile) : null);
}
