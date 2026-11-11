namespace PocketCsvReader.Configuration;

/// <summary>Selects Hive-style partition discovery for a configured format reader.</summary>
public sealed class FlatFileDirectoryBuilder
{
    private readonly FlatFileQueryReader _reader;
    private readonly Func<FileNamePartitionPattern?, FileQuerySelection<NamedPartitions>> _select;

    internal FlatFileDirectoryBuilder(FlatFileQueryReader reader, HivePartitionSource source, Func<string, Stream>? open)
        => (_reader, _select) = (reader, pattern => new(conditions => source.Discover(conditions, pattern, open)));

    internal FlatFileDirectoryBuilder(FlatFileQueryReader reader, AsyncHivePartitionSource source)
        => (_reader, _select) = (reader, pattern => new(null, new(
            conditions => source.Discover(conditions, pattern), source.Open)));

    /// <summary>Inherits named partitions from key=value directories below the root.</summary>
    public FlatFileQuery<NamedPartitions> HiveStyle() => new(_reader, _select(null));

    /// <summary>Combines inherited directory partitions with additional captures from each filename.</summary>
    public FlatFileQuery<NamedPartitions> HiveStyle(string fileNamePattern)
        => new(_reader, _select(new FileNamePartitionPattern(fileNamePattern)));
}
