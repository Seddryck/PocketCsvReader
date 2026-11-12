namespace PocketCsvReader.Indexing;

/// <summary>Defines a hash index for direct equality lookups on a CSV key.</summary>
public sealed class HashIndex
{
    public HashIndex(IIndexKey key)
        : this(key, requiresIndex: true)
    { }

    private HashIndex(IIndexKey key, bool requiresIndex)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        RequiresIndex = requiresIndex;
    }

    public IIndexKey Key { get; }
    internal bool RequiresIndex { get; }

    /// <summary>Requires a usable sidecar for each source. This is the default.</summary>
    public HashIndex WithRequiredIndex() => RequiresIndex ? this : new(Key, requiresIndex: true);

    /// <summary>Scans sources whose sidecars are missing or whose reader configuration cannot use them.</summary>
    public HashIndex WithOptionalIndex() => RequiresIndex ? new(Key, requiresIndex: false) : this;
}
