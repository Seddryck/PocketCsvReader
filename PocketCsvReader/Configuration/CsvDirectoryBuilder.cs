namespace PocketCsvReader.Configuration;

/// <summary>Selects Hive-style partition discovery beneath a root directory.</summary>
public sealed class CsvDirectoryBuilder
{
    private readonly CsvReaderBuilder _reader;
    private readonly HivePartitionSource _source;
    private readonly Func<string, Stream>? _open;

    internal CsvDirectoryBuilder(CsvReaderBuilder reader, HivePartitionSource source, Func<string, Stream>? open)
        => (_reader, _source, _open) = (reader, source, open);

    public CsvFileQueryBuilder<NamedPartitions> WithPartitions(
        Func<CsvDirectoryBuilder, CsvFileQueryBuilder<NamedPartitions>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return configure(this) ?? throw new InvalidOperationException("Partition configuration returned null.");
    }

    /// <summary>Inherits named partitions from key=value directories below the root.</summary>
    public CsvFileQueryBuilder<NamedPartitions> HiveStyle()
        => new(_reader, conditions => _source.Discover(conditions, null, _open));

    /// <summary>Combines inherited directory partitions with additional captures from each filename.</summary>
    public CsvFileQueryBuilder<NamedPartitions> HiveStyle(string fileNamePattern)
    {
        var pattern = new FileNamePartitionPattern(fileNamePattern);
        return new(_reader, conditions => _source.Discover(conditions, pattern, _open));
    }
}
