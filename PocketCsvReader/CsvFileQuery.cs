using System.Data.Common;
using System.Runtime.CompilerServices;

namespace PocketCsvReader;

/// <summary>Discovers partitioned CSV files and creates independent readers over them.</summary>
public sealed class CsvFileQuery<TPartition>
{
    private readonly Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>>? _discover;
    private readonly AsyncFileDiscovery<TPartition>? _asyncSource;
    private readonly FileFilter[] _filters;
    private readonly Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? _createReader;
    internal CsvProfile Profile { get; }

    internal CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        : this(profile, discover, [])
    {
    }

    internal CsvFileQuery(CsvProfile profile, AsyncFileDiscovery<TPartition> asyncSource)
        : this(profile, null, [], asyncSource: asyncSource)
    {
    }

    private CsvFileQuery(CsvProfile profile,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>>? discover, FileFilter[] filters,
        Func<PartitionedFile<TPartition>, Stream, CsvDataReader>? createReader = null,
        AsyncFileDiscovery<TPartition>? asyncSource = null)
        => (Profile, _discover, _filters, _createReader, _asyncSource) = (profile, discover, filters, createReader, asyncSource);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public CsvFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(Profile, _discover, [.. _filters, new(predicate, null)], _createReader, _asyncSource);
    }

    internal CsvFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(Profile, _discover, [.. _filters, new(predicate, conditions)], _createReader, _asyncSource);

    internal CsvFileQuery<TPartition> WithReaderFactory(Func<PartitionedFile<TPartition>, Stream, CsvDataReader> createReader)
    {
        if (_createReader is not null)
            throw new InvalidOperationException("The query already has a custom reader factory.");
        return new(Profile, _discover, _filters, createReader, _asyncSource);
    }

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles()
    {
        if (_discover is null)
            throw new NotSupportedException("This directory source requires EnumerateFilesAsync.");

        return _discover(GetPruningConditions()).Select(RequireDescriptor).Where(Accepts);
    }

    /// <summary>Discovers matching file descriptors without opening their input streams.</summary>
    public async IAsyncEnumerable<PartitionedFile<TPartition>> EnumerateFilesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_asyncSource is { } source)
        {
            var next = source.Discover(GetPruningConditions());
            while (await NextAcceptedAsync(next, cancellationToken).ConfigureAwait(false) is { } file)
                yield return file;
            yield break;
        }

        foreach (var file in EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    /// <summary>Creates a batch that starts opening matching files on the first read.</summary>
    public CsvBatchDataReader ToDataReader()
    {
        if (_filters.Length != 0 && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            throw new InvalidOperationException("Partition filtering requires a header in each file when headers are enabled.");

        if (_asyncSource is not { } source)
            return CsvBatchDataReader.FromDeferredInputs(EnumerateInputs(), Profile);

        var next = source.Discover(GetPruningConditions());
        return CsvBatchDataReader.FromAsyncInputs(NextInputAsync, Profile);

        async ValueTask<CsvBatchDataReader.ReaderInput?> NextInputAsync(CancellationToken token)
        {
            if (await NextAcceptedAsync(next, token).ConfigureAwait(false) is not { } file)
                return null;

            var stream = await source.Open(file.Path, token).ConfigureAwait(false);
            return CreateInput(file, stream);
        }
    }

    /// <summary>Creates a database reader that starts opening matching files on the first row request.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

    private static PartitionedFile<TPartition> RequireDescriptor(PartitionedFile<TPartition>? file)
        => file ?? throw new ArgumentException("The file sequence contains a null descriptor.");

    private PartitionCondition[] GetPruningConditions()
        // Conditions after a delegate cannot move ahead of it. The delegate may throw or reject the file.
        => _filters.TakeWhile(filter => filter.Conditions is not null)
            .SelectMany(filter => filter.Conditions ?? []).ToArray();

    private bool Accepts(PartitionedFile<TPartition> file)
        => _filters.All(filter => filter.Match(file.Partition));

    private async ValueTask<PartitionedFile<TPartition>?> NextAcceptedAsync(
        Func<CancellationToken, ValueTask<PartitionedFile<TPartition>?>> next, CancellationToken token)
    {
        while (await next(token).ConfigureAwait(false) is { } file)
        {
            token.ThrowIfCancellationRequested();
            if (Accepts(file))
                return file;
        }
        return null;
    }

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

    private sealed record FileFilter(Func<TPartition, bool> Match, PartitionCondition[]? Conditions);
}

internal sealed record AsyncFileDiscovery<TPartition>(
    Func<IReadOnlyList<PartitionCondition>, Func<CancellationToken, ValueTask<PartitionedFile<TPartition>?>>> Discover,
    Func<string, CancellationToken, ValueTask<Stream>> Open);
