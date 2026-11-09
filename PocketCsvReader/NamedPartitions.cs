using System.Collections.ObjectModel;

namespace PocketCsvReader;

/// <summary>A read-only set of ordinal, case-sensitive string partition keys and values.</summary>
public sealed class NamedPartitions : ReadOnlyDictionary<string, string>
{
    public NamedPartitions(IEnumerable<KeyValuePair<string, string>> values)
        : base(Copy(values))
    {
    }

    private NamedPartitions(Dictionary<string, string> owned)
        : base(owned)
    {
    }

    internal NamedPartitions WithValues(IEnumerable<KeyValuePair<string, string>> values, string path)
    {
        var combined = new Dictionary<string, string>(this, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!combined.TryAdd(key, value))
                throw new FormatException($"Duplicate partition key '{key}' at '{path}'.");
        }
        return new NamedPartitions(combined);
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
