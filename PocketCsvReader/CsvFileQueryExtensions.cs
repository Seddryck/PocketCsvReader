namespace PocketCsvReader;

/// <summary>Filters CSV file queries that expose named partition values.</summary>
public static class CsvFileQueryExtensions
{
    /// <summary>Keeps files that satisfy every condition in the supplied order.</summary>
    public static CsvFileQuery<NamedPartitions> WithPartitionFilter(
        this CsvFileQuery<NamedPartitions> query, params PartitionCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(conditions);
        var snapshot = conditions.ToArray();
        if (snapshot.Any(condition => condition is null))
            throw new ArgumentException("The conditions array contains a null entry.", nameof(conditions));

        return snapshot.Length == 0 ? query : query.WithNamedFilter(
            partitions => snapshot.All(condition => condition.Matches(partitions[condition.Key])), snapshot);
    }
}
