namespace PocketCsvReader.Configuration;

/// <summary>Configures partition discovery for an input path sequence.</summary>
public sealed class FilePartitionBuilder
{
    private readonly CsvReaderBuilder _reader;
    private readonly IEnumerable<string> _paths;
    private readonly Func<string, Stream>? _open;

    internal FilePartitionBuilder(CsvReaderBuilder reader, IEnumerable<string> paths, Func<string, Stream>? open)
        => (_reader, _paths, _open) = (reader, paths, open);

    /// <summary>Configures a selector that is evaluated as paths are discovered.</summary>
    public CsvFileQueryBuilder<TPartition> FromPath<TPartition>(Func<string, TPartition> select)
    {
        ArgumentNullException.ThrowIfNull(select);
        return new(_reader, Discover());

        IEnumerable<PartitionedFile<TPartition>> Discover()
        {
            foreach (var path in _paths)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(path);
                yield return new PartitionedFile<TPartition>(
                    path, select(path), _open is null ? null : () => _open(path));
            }
        }
    }

    /// <summary>Extracts named string partitions from each complete filename.</summary>
    public CsvFileQueryBuilder<NamedPartitions> FromFileName(string pattern)
        => FromPath(new FileNamePartitionPattern(pattern).Extract);
}

/// <summary>Selects how a CSV path sequence supplies partition metadata.</summary>
public sealed class CsvFilePathBuilder
{
    private readonly FilePartitionBuilder _partitions;

    internal CsvFilePathBuilder(FilePartitionBuilder partitions) => _partitions = partitions;

    public CsvFileQueryBuilder<TPartition> WithPartitions<TPartition>(
        Func<FilePartitionBuilder, CsvFileQueryBuilder<TPartition>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return configure(_partitions) ?? throw new InvalidOperationException("Partition configuration returned null.");
    }
}

/// <summary>Builds a CSV query over files with partition metadata.</summary>
public sealed class CsvFileQueryBuilder<TPartition>
{
    private readonly Func<CsvFileQuery<TPartition>> _build;

    internal CsvFileQueryBuilder(CsvReaderBuilder reader, IEnumerable<PartitionedFile<TPartition>> files)
        : this(reader, _ => files)
    {
    }

    internal CsvFileQueryBuilder(CsvReaderBuilder reader,
        Func<IReadOnlyList<PartitionCondition>, IEnumerable<PartitionedFile<TPartition>>> discover)
        => _build = () => new(reader.Build().Profile, discover);

    internal CsvFileQueryBuilder(CsvReaderBuilder reader, AsyncFileDiscovery<TPartition> discover)
        => _build = () => new(reader.Build().Profile, discover);

    public CsvFileQuery<TPartition> Build() => _build();
}
