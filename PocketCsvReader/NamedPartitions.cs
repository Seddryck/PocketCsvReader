using System.Collections.ObjectModel;

namespace PocketCsvReader;

/// <summary>A read-only set of ordinal, case-sensitive string partition keys and values.</summary>
public sealed class NamedPartitions : ReadOnlyDictionary<string, string>
{
    public NamedPartitions(IEnumerable<KeyValuePair<string, string>> values)
        : base(Copy(values))
    {
    }

    private static Dictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            result.Add(key, value);
        }
        return result;
    }
}
