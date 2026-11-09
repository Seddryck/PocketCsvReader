namespace PocketCsvReader;

/// <summary>Creates format-neutral file descriptors.</summary>
public static class FileSource
{
    /// <summary>Associates a path with metadata that applies to every row in the file.</summary>
    public static PartitionedFile<TPartition> Partitioned<TPartition>(string path, TPartition partition)
        => new(path, partition, null);

    /// <summary>Associates a path and metadata with a deferred stream opener.</summary>
    public static PartitionedFile<TPartition> Partitioned<TPartition>(
        string path, TPartition partition, Func<Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return new(path, partition, open);
    }
}

/// <summary>A file path and its associated partition metadata.</summary>
public sealed class PartitionedFile<TPartition>
{
    internal PartitionedFile(string path, TPartition partition, Func<Stream>? open)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(partition);
        Path = path;
        Partition = partition;
        Opener = open;
    }

    public string Path { get; }

    public TPartition Partition { get; }

    internal Func<Stream>? Opener { get; }
}
