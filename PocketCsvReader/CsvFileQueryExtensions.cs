namespace PocketCsvReader;

/// <summary>Filters CSV file queries that expose named partition values.</summary>
public static class CsvFileQueryExtensions
{
    /// <summary>Keeps files that satisfy every condition in the supplied order.</summary>
    public static CsvFileQuery<NamedPartitions> WithPartitionFilter(
        this CsvFileQuery<NamedPartitions> query, params PartitionCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = NamedPartitionFilter.Snapshot(conditions);
        return snapshot.Length == 0 ? query : query.WithNamedFilter(
            partitions => NamedPartitionFilter.Accepts(partitions, snapshot), snapshot);
    }
}
