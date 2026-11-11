using PocketCsvReader.Configuration;

namespace PocketCsvReader;

/// <summary>Creates partitioned file queries for configured format readers.</summary>
public static class FlatFileQueryExtensions
{
    /// <summary>Selects files beneath a local root directory.</summary>
    public static FlatFileDirectoryBuilder FromDirectory<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> reader, string root, string searchPattern)
        where TProfile : IProfile
        where TDataReader : BaseDataReader<TProfile>
        => CreateDirectory(reader, root, searchPattern, open: null);

    /// <summary>Selects files beneath a local root and defers opening the accepted files.</summary>
    public static FlatFileDirectoryBuilder FromDirectory<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> reader, string root, string searchPattern, Func<string, Stream> open)
        where TProfile : IProfile
        where TDataReader : BaseDataReader<TProfile>
    {
        ArgumentNullException.ThrowIfNull(open);
        return CreateDirectory(reader, root, searchPattern, open);
    }

    /// <summary>Selects files with existing partition metadata.</summary>
    public static FlatFileQuery<TPartition> FromFiles<TProfile, TDataReader, TPartition>(
        this FlatFileReader<TProfile, TDataReader> reader, IEnumerable<PartitionedFile<TPartition>> files)
        where TProfile : IProfile
        where TDataReader : BaseDataReader<TProfile>
    {
        ArgumentNullException.ThrowIfNull(files);
        return new(CreateQueryReader(reader), new(_ => files));
    }

    /// <summary>Keeps files that satisfy every condition in the supplied order.</summary>
    public static FlatFileQuery<NamedPartitions> WithPartitionFilter(
        this FlatFileQuery<NamedPartitions> query, params PartitionCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = NamedPartitionFilter.Snapshot(conditions);
        return snapshot.Length == 0 ? query : query.WithNamedFilter(
            partitions => NamedPartitionFilter.Accepts(partitions, snapshot), snapshot);
    }

    private static FlatFileDirectoryBuilder CreateDirectory<TProfile, TDataReader>(
        FlatFileReader<TProfile, TDataReader> reader, string root, string searchPattern, Func<string, Stream>? open)
        where TProfile : IProfile
        where TDataReader : BaseDataReader<TProfile>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        HivePartitionSource.ValidateSearchPattern(searchPattern);
        return new(CreateQueryReader(reader), new HivePartitionSource(Path.GetFullPath(root), searchPattern), open);
    }

    internal static FlatFileQueryReader CreateQueryReader<TProfile, TDataReader>(FlatFileReader<TProfile, TDataReader> reader)
        where TProfile : IProfile
        where TDataReader : BaseDataReader<TProfile>
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(reader.Profile, reader.FileBufferSize, (stream, window, filters) =>
        {
            var input = reader.ToDataReader(stream);
            input.Window = window;
            input.FileQueryFilters = filters;
            return input;
        });
    }
}
