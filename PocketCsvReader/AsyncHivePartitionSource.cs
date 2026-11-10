using System.IO.Enumeration;

namespace PocketCsvReader;

internal sealed class AsyncHivePartitionSource(
    string root, string searchPattern,
    Func<string, CancellationToken, ValueTask<List<AsyncHivePartitionSource.DirectoryEntry>>> list,
    Func<string, CancellationToken, ValueTask<Stream>> open)
{
    internal readonly record struct DirectoryEntry(string Path, bool IsDirectory);

    internal Func<string, CancellationToken, ValueTask<Stream>> Open => open;

    internal Func<CancellationToken, ValueTask<PartitionedFile<NamedPartitions>?>> Discover(
        IReadOnlyList<PartitionCondition> conditions, FileNamePartitionPattern? pattern)
    {
        var pending = new Stack<(string Path, NamedPartitions Parent)>();
        var partitions = HivePartitionSource.Empty;
        List<DirectoryEntry>? entries = null;
        var index = 0;
        pending.Push((root, partitions));

        return NextAsync;

        async ValueTask<PartitionedFile<NamedPartitions>?> NextAsync(CancellationToken token)
        {
            while (true)
            {
                if (FindNextFile(entries, ref index, partitions, pattern, token) is { } file)
                    return file;

                token.ThrowIfCancellationRequested();
                entries = null;
                if (!pending.TryPop(out var directory))
                    return null;

                partitions = directory.Path == root
                    ? directory.Parent
                    : HivePartitionSource.AddDirectory(directory.Parent, GetDirectoryName(directory.Path), directory.Path);
                if (HivePartitionSource.ShouldPrune(partitions, conditions))
                    continue;

                entries = await list(directory.Path, token).ConfigureAwait(false);
                index = 0;
                QueueChildren(entries, pending, partitions);
            }
        }
    }

    private PartitionedFile<NamedPartitions>? FindNextFile(
        List<DirectoryEntry>? entries, ref int index, NamedPartitions partitions,
        FileNamePartitionPattern? pattern, CancellationToken token)
    {
        while (entries is not null && index < entries.Count)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[index++];
            if (entry.IsDirectory)
                continue;

            var name = GetName(entry.Path);
            if (!FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: false))
                continue;

            var metadata = pattern is null
                ? partitions
                : partitions.WithValues(pattern.Extract(name, entry.Path), entry.Path);
            return new(entry.Path, metadata, null);
        }
        return null;
    }

    private static void QueueChildren(
        List<DirectoryEntry> entries, Stack<(string Path, NamedPartitions Parent)> pending, NamedPartitions partitions)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].IsDirectory)
                pending.Push((entries[i].Path, partitions));
        }
    }

    private static string GetName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string GetDirectoryName(string path) => GetName(path.EndsWith('/') ? path[..^1] : path);
}
