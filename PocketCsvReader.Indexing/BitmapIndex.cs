using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing;

/// <summary>Defines the categorical columns of a CSV bitmap index.</summary>
public sealed class BitmapIndex
{
    public BitmapIndex(params IIndexKey[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0)
            throw new ArgumentException("A bitmap index requires at least one key.", nameof(keys));

        var columns = new HashSet<(string?, int?)>();
        var snapshot = (IIndexKey[])keys.Clone();
        foreach (var key in snapshot)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!columns.Add((key.ColumnName, key.ColumnOrdinal)))
                throw new ArgumentException("A bitmap index cannot define the same column twice.", nameof(keys));
        }
        Keys = Array.AsReadOnly(snapshot);
        RequiresIndex = true;
    }

    private BitmapIndex(IReadOnlyList<IIndexKey> keys, bool requiresIndex)
        => (Keys, RequiresIndex) = (keys, requiresIndex);

    public IReadOnlyList<IIndexKey> Keys { get; }
    internal bool RequiresIndex { get; }

    internal IIndexKey[] ResolveKeys(RuntimeParsersDescriptor? parsers)
        => Keys.Select(key => key.ResolveRuntimeParser(parsers)).ToArray();

    /// <summary>Requires a usable sidecar for each source. This is the default.</summary>
    public BitmapIndex WithRequiredIndex() => RequiresIndex ? this : new(Keys, requiresIndex: true);

    /// <summary>Scans sources whose sidecars are missing or whose reader configuration cannot use them.</summary>
    public BitmapIndex WithOptionalIndex() => RequiresIndex ? new(Keys, requiresIndex: false) : this;
}
