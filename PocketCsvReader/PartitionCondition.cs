namespace PocketCsvReader;

/// <summary>An ordinal comparison against a named string partition value.</summary>
public sealed class PartitionCondition
{
    private readonly bool _equal;

    private PartitionCondition(string key, string value, bool equal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        (Key, Value, _equal) = (key, value, equal);
    }

    public string Key { get; }

    public string Value { get; }

    public static PartitionCondition Equal(string key, string value) => new(key, value, equal: true);

    public static PartitionCondition NotEqual(string key, string value) => new(key, value, equal: false);

    internal bool Matches(string value) => string.Equals(value, Value, StringComparison.Ordinal) == _equal;
}
