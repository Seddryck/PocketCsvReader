namespace PocketCsvReader.Configuration;

/// <summary>Selects Hive-style partition discovery beneath a root directory.</summary>
public sealed class CsvDirectoryBuilder
{
    private readonly Func<FileNamePartitionPattern?, CsvFileQueryBuilder<NamedPartitions>> _configure;

    internal CsvDirectoryBuilder(CsvReaderBuilder reader, HivePartitionSource source, Func<string, Stream>? open)
        => _configure = pattern => new(reader, conditions => source.Discover(conditions, pattern, open));

    internal CsvDirectoryBuilder(CsvReaderBuilder reader, AsyncHivePartitionSource source)
        => _configure = pattern => new(reader, new AsyncFileDiscovery<NamedPartitions>(
            conditions => source.Discover(conditions, pattern), source.Open));

    public CsvFileQueryBuilder<NamedPartitions> WithPartitions(
        Func<CsvDirectoryBuilder, CsvFileQueryBuilder<NamedPartitions>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return configure(this) ?? throw new InvalidOperationException("Partition configuration returned null.");
    }

    /// <summary>Inherits named partitions from key=value directories below the root.</summary>
    public CsvFileQueryBuilder<NamedPartitions> HiveStyle()
        => _configure(null);

    /// <summary>Combines inherited directory partitions with additional captures from each filename.</summary>
    public CsvFileQueryBuilder<NamedPartitions> HiveStyle(string fileNamePattern)
        => _configure(new FileNamePartitionPattern(fileNamePattern));
}
