using System.Data.Common;

namespace PocketCsvReader;

/// <summary>Discovers partitioned files and reads their rows with a configured format reader.</summary>
public sealed class FlatFileQuery<TPartition>
{
    private readonly FlatFileQueryReader _reader;
    private readonly FileQuerySelection<TPartition> _selection;
    private readonly IReadOnlyList<ReaderFilter> _filters;

    internal FlatFileQuery(FlatFileQueryReader reader, FileQuerySelection<TPartition> selection)
        : this(reader, selection, reader.Profile.Filters)
    {
    }

    private FlatFileQuery(FlatFileQueryReader reader, FileQuerySelection<TPartition> selection, IReadOnlyList<ReaderFilter> filters)
        => (_reader, _selection, _filters) = (reader, selection, filters);

    /// <summary>Keeps files accepted by a predicate over their complete partition metadata.</summary>
    public FlatFileQuery<TPartition> WithPartitionFilter(Func<TPartition, bool> predicate)
        => new(_reader, _selection.WithFilter(predicate), _filters);

    internal FlatFileQuery<TPartition> WithNamedFilter(Func<TPartition, bool> predicate, PartitionCondition[] conditions)
        => new(_reader, _selection.WithFilter(predicate, conditions), _filters);

    /// <summary>Keeps rows whose sanitized cell equals the expected text.</summary>
    public FlatFileQuery<TPartition> WithFilter(string column, string expected, StringComparison comparison = StringComparison.Ordinal)
        => WithFilter(column, ReaderFilter.TextEquals(expected, comparison));

    /// <inheritdoc cref="WithFilter(string,string,StringComparison)"/>
    public FlatFileQuery<TPartition> WithFilter(int ordinal, string expected, StringComparison comparison = StringComparison.Ordinal)
        => WithFilter(ordinal, ReaderFilter.TextEquals(expected, comparison));

    /// <summary>Keeps rows accepted by a sanitized-span predicate.</summary>
    public FlatFileQuery<TPartition> WithFilter(string column, ParseSpan<bool> predicate)
        => AddFilter(ReaderFilter.ForCell(column, predicate));

    /// <inheritdoc cref="WithFilter(string,ParseSpan{bool})"/>
    public FlatFileQuery<TPartition> WithFilter(int ordinal, ParseSpan<bool> predicate)
        => AddFilter(ReaderFilter.ForCell(ordinal, predicate));

    /// <summary>Keeps rows accepted by a typed predicate using the getter's parsing rules.</summary>
    public FlatFileQuery<TPartition> WithFilter<T>(string column, Func<T, bool> predicate)
        => AddFilter(ReaderFilter.ForCell(column, predicate));

    /// <inheritdoc cref="WithFilter{T}(string,Func{T,bool})"/>
    public FlatFileQuery<TPartition> WithFilter<T>(int ordinal, Func<T, bool> predicate)
        => AddFilter(ReaderFilter.ForCell(ordinal, predicate));

    /// <summary>Adds a read-only row predicate. Conditions run in registration order and short-circuit.</summary>
    public FlatFileQuery<TPartition> WithRowFilter(Func<IRowView, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return AddFilter(new(predicate));
    }

    private FlatFileQuery<TPartition> AddFilter(ReaderFilter filter) => new(_reader, _selection, [.. _filters, filter]);

    /// <summary>Discovers matching files without opening their streams.</summary>
    public IEnumerable<PartitionedFile<TPartition>> EnumerateFiles() => _selection.EnumerateFiles();

    /// <summary>Discovers matching files without opening their streams.</summary>
    public IAsyncEnumerable<PartitionedFile<TPartition>> EnumerateFilesAsync(CancellationToken cancellationToken = default)
        => _selection.EnumerateFilesAsync(cancellationToken);

    /// <summary>Opens matching files on demand and reads them in discovery order.</summary>
    /// <remarks>
    /// Each file has its own parser and field layout. Skip and take apply across the matching files.
    /// Asynchronous directory sources require <see cref="IAsyncDataReader.ReadAsync"/>.
    /// </remarks>
    public IAsyncDataReader ToDataReader()
    {
        var window = RecordWindow.Create(_reader.Profile);
        if (_selection.RequiresAsync)
        {
            var next = _selection.CreateAsyncDiscovery();
            return new FlatFileBatchDataReader(OpenNextAsync, window, requiresAsync: true);

            async ValueTask<IAsyncDataReader?> OpenNextAsync(CancellationToken token)
            {
                if (await next(token).ConfigureAwait(false) is not { } file)
                    return null;
                var stream = await _selection.OpenAsync(file, token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    return CreateReader(stream, window);
                }
                catch
                {
                    if (stream is not null)
                        await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }

        IEnumerator<PartitionedFile<TPartition>>? files = null;
        return new FlatFileBatchDataReader(OpenNext, window, requiresAsync: false, () => files?.Dispose());

        ValueTask<IAsyncDataReader?> OpenNext(CancellationToken token)
        {
            files ??= EnumerateFiles().GetEnumerator();
            if (!files.MoveNext())
                return ValueTask.FromResult<IAsyncDataReader?>(null);

            token.ThrowIfCancellationRequested();
            var file = files.Current;
            var stream = file.Opener is { } open ? open() : new FileStream(file.Path, FileMode.Open,
                FileAccess.Read, FileShare.Read, _reader.BufferSize, useAsync: true);
            try
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IAsyncDataReader?>(CreateReader(stream, window));
            }
            catch
            {
                stream?.Dispose();
                throw;
            }
        }
    }

    /// <summary>Creates a database reader that opens matching files on demand.</summary>
    public DbDataReader ToDbDataReader() => new DbDataReaderAdapter(ToDataReader());

    private IAsyncDataReader CreateReader(Stream stream, RecordWindow? window)
    {
        if (stream is null || !stream.CanRead)
            throw new InvalidOperationException("The file opener must return a readable stream.");
        return _reader.Create(stream, window, _filters);
    }
}

internal sealed record FlatFileQueryReader(IProfile Profile, int BufferSize,
    Func<Stream, RecordWindow?, IReadOnlyList<ReaderFilter>, IAsyncDataReader> Create);
