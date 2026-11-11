namespace PocketCsvReader.Indexing;

/// <summary>Defines a per-file Bloom filter for equality lookups on an unsorted key.</summary>
public sealed class BloomIndex
{
    /// <summary>Sets the key and the bitset sizing settings used when building sidecars.</summary>
    public BloomIndex(IIndexKey key, int expectedDistinctCount = 10_000, double falsePositiveProbability = 0.01,
        int maxBytes = 1024 * 1024)
        : this(key, expectedDistinctCount, falsePositiveProbability, maxBytes, requiresIndex: true)
    { }

    private BloomIndex(IIndexKey key, int expectedDistinctCount, double falsePositiveProbability, int maxBytes,
        bool requiresIndex)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDistinctCount);
        if (!(falsePositiveProbability > 0 && falsePositiveProbability < 1))
            throw new ArgumentOutOfRangeException(nameof(falsePositiveProbability));
        if (maxBytes is < BlockBytes or > MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        Key = key;
        ExpectedDistinctCount = expectedDistinctCount;
        FalsePositiveProbability = falsePositiveProbability;
        MaxBytes = maxBytes;
        RequiresIndex = requiresIndex;
    }

    internal const int BlockBytes = 32;
    internal const int MaxPayloadBytes = 128 * 1024 * 1024;

    public IIndexKey Key { get; }
    public int ExpectedDistinctCount { get; }
    public double FalsePositiveProbability { get; }
    public int MaxBytes { get; }
    internal bool RequiresIndex { get; }

    internal int PayloadBytes
    {
        get
        {
            var ln2 = Math.Log(2);
            var blocks = Math.Ceiling(-ExpectedDistinctCount * Math.Log(FalsePositiveProbability)
                / (ln2 * ln2 * 8 * BlockBytes));
            return (int)Math.Clamp(blocks, 1, MaxBytes / BlockBytes) * BlockBytes;
        }
    }

    /// <summary>Requires a usable sidecar for each source. This is the default.</summary>
    public BloomIndex WithRequiredIndex() => RequiresIndex ? this
        : new(Key, ExpectedDistinctCount, FalsePositiveProbability, MaxBytes, requiresIndex: true);

    /// <summary>Scans sources whose sidecars are missing or whose reader configuration cannot use them.</summary>
    public BloomIndex WithOptionalIndex() => RequiresIndex
        ? new(Key, ExpectedDistinctCount, FalsePositiveProbability, MaxBytes, requiresIndex: false) : this;
}
