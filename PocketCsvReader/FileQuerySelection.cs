using System.Runtime.CompilerServices;

namespace PocketCsvReader;

internal sealed class FileQuerySelection<TPartition>(
    Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>>? discover,
    AsyncFileDiscovery<TPartition>? asyncSource = null,
    FileQuerySelection<TPartition>.Filter[]? filters = null)
{
    private readonly Filter[] _filters = filters ?? [];
    internal bool HasFilters => _filters.Length != 0;
    internal bool RequiresAsync => asyncSource is not null;

    internal FileQuerySelection<TPartition> WithFilter(Func<TPartition, bool> predicate, PartitionCondition[]? conditions = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(discover, asyncSource, [.. _filters, new(predicate, conditions)]);
    }

    internal IEnumerable<PartitionedFile<TPartition>> EnumerateFiles()
    {
        if (discover is null)
            throw new NotSupportedException("This directory source requires EnumerateFilesAsync.");

        return discover(GetPruningConditions()).Select(RequireDescriptor).Where(Accepts);
    }

    internal async IAsyncEnumerable<PartitionedFile<TPartition>> EnumerateFilesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asyncSource is not null)
        {
            var next = CreateAsyncDiscovery();
            while (await next(cancellationToken).ConfigureAwait(false) is { } file)
                yield return file;
            yield break;
        }

        foreach (var file in EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    internal Func<CancellationToken, ValueTask<PartitionedFile<TPartition>?>> CreateAsyncDiscovery()
    {
        var next = asyncSource!.Discover(GetPruningConditions());
        return NextAcceptedAsync;

        async ValueTask<PartitionedFile<TPartition>?> NextAcceptedAsync(CancellationToken token)
        {
            while (await next(token).ConfigureAwait(false) is { } file)
            {
                token.ThrowIfCancellationRequested();
                if (Accepts(file))
                    return file;
            }
            return null;
        }
    }

    internal ValueTask<Stream> OpenAsync(PartitionedFile<TPartition> file, CancellationToken token)
        => asyncSource!.Open(file.Path, token);

    private static PartitionedFile<TPartition> RequireDescriptor(PartitionedFile<TPartition>? file)
        => file ?? throw new ArgumentException("The file sequence contains a null descriptor.");

    private PartitionCondition[] GetPruningConditions()
        // A named condition cannot run ahead of a delegate that may throw or reject the file.
        => _filters.TakeWhile(filter => filter.Conditions is not null)
            .SelectMany(filter => filter.Conditions ?? []).ToArray();

    private bool Accepts(PartitionedFile<TPartition> file)
        => _filters.All(filter => filter.Match(file.Partition));

    internal sealed record Filter(Func<TPartition, bool> Match, PartitionCondition[]? Conditions);
}

internal sealed record AsyncFileDiscovery<TPartition>(
    Func<IReadOnlyList<PartitionCondition>, Func<CancellationToken, ValueTask<PartitionedFile<TPartition>?>>> Discover,
    Func<string, CancellationToken, ValueTask<Stream>> Open);

internal static class NamedPartitionFilter
{
    internal static PartitionCondition[] Snapshot(PartitionCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var snapshot = conditions.ToArray();
        if (snapshot.Any(condition => condition is null))
            throw new ArgumentException("The conditions array contains a null entry.", nameof(conditions));
        return snapshot;
    }

    internal static bool Accepts(NamedPartitions partitions, PartitionCondition[] conditions)
        => conditions.All(condition => condition.Matches(partitions[condition.Key]));
}
