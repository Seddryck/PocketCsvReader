namespace PocketCsvReader.Indexing;

/// <summary>Defines a CSV zone map over consecutive blocks of data records.</summary>
public sealed class ZoneMapIndex
{
    /// <summary>Sets the indexed key and the positive number of data records per block.</summary>
    public ZoneMapIndex(IIndexKey key, int blockSize)
        : this(key, blockSize, requiresIndex: true)
    { }

    private ZoneMapIndex(IIndexKey key, int blockSize, bool requiresIndex)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        Key = key;
        BlockSize = blockSize;
        RequiresIndex = requiresIndex;
    }

    public IIndexKey Key { get; }
    public int BlockSize { get; }
    internal bool RequiresIndex { get; }

    /// <summary>Requires a usable sidecar for each source. This is the default.</summary>
    public ZoneMapIndex WithRequiredIndex() => RequiresIndex ? this : new(Key, BlockSize, requiresIndex: true);

    /// <summary>Scans sources whose sidecars are missing or whose reader configuration cannot use them.</summary>
    public ZoneMapIndex WithOptionalIndex() => RequiresIndex ? new(Key, BlockSize, requiresIndex: false) : this;
}

/// <summary>Defines the lower and upper endpoints of a typed zone-map lookup.</summary>
public sealed class IndexRange<TKey>
{
    public IndexRange(TKey lower, TKey upper, bool includeLower = true, bool includeUpper = true)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        Lower = lower;
        Upper = upper;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    public TKey Lower { get; }
    public TKey Upper { get; }
    public bool IncludeLower { get; }
    public bool IncludeUpper { get; }
}

internal readonly struct ZoneMapPredicate
{
    private readonly object _lower;
    private readonly object? _upper;
    private readonly bool _includeLower;
    private readonly bool _includeUpper;

    internal IIndexKey Key { get; }

    private ZoneMapPredicate(IIndexKey key, object lower, object? upper, bool includeLower, bool includeUpper)
        => (Key, _lower, _upper, _includeLower, _includeUpper) = (key, lower, upper, includeLower, includeUpper);

    internal static ZoneMapPredicate EqualTo<TKey>(IIndexKey key, TKey value)
        => new(key, ReaderLookup.ValidateKey(key, value), null, true, true);

    internal static ZoneMapPredicate InRange<TKey>(IIndexKey key, IndexRange<TKey> range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var lower = ReaderLookup.ValidateKey(key, range.Lower);
        var upper = ReaderLookup.ValidateKey(key, range.Upper);
        if (key.Compare(lower, upper) > 0)
            throw new ArgumentException("The lower endpoint must not exceed the upper endpoint.", nameof(range));
        return new(key, lower, upper, range.IncludeLower, range.IncludeUpper);
    }

    internal ZoneMapPredicate WithKey(IIndexKey key) => new(key, _lower, _upper, _includeLower, _includeUpper);

    internal bool Contains(object value) => _upper is null ? Key.Compare(value, _lower) == 0 : Intersects(value, value);

    internal bool Intersects(object min, object max)
    {
        var lowerComparison = Key.Compare(max, _lower);
        if (lowerComparison < 0 || (lowerComparison == 0 && !_includeLower))
            return false;
        var upperComparison = Key.Compare(min, _upper ?? _lower);
        return upperComparison < 0 || (upperComparison == 0 && _includeUpper);
    }

    internal RecordSelection Select(NullableSpan field)
        => field.HasValue && Key.Parse(field.Value) is { } parsed && Contains(parsed)
            ? RecordSelection.Include : RecordSelection.Skip;
}
