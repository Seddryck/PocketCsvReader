namespace PocketCsvReader;

// Each entry's string is both the owned key and the return value.
internal sealed class DecodedStringPool
{
    private const int BucketCount = 4096;
    private const int EntriesPerBucket = 4;
    private const int MaxFieldLength = 128;
    private const int MaxHintedColumns = BucketCount * EntriesPerBucket;

    private Entry[]? _entries;
    private int[]? _lastByColumn;

    public string GetString(int column, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return string.Empty;
        if (text.Length > MaxFieldLength)
            return text.ToString();

        var entries = _entries ??= new Entry[BucketCount * EntriesPerBucket];
        var lastByColumn = _lastByColumn ??= new int[32];
        if ((uint)column < (uint)lastByColumn.Length)
            return GetWithHint(text, entries, ref lastByColumn[column]);
        if ((uint)column >= MaxHintedColumns)
        {
            int ignored = 0;
            return GetOrAdd(text, entries, ref ignored);
        }

        int capacity = Math.Min(MaxHintedColumns, Math.Max(lastByColumn.Length * 2, column + 1));
        Array.Resize(ref lastByColumn, capacity);
        _lastByColumn = lastByColumn;
        return GetWithHint(text, entries, ref lastByColumn[column]);
    }

    private static string GetWithHint(ReadOnlySpan<char> text, Entry[] entries, ref int last)
    {
        // One-based slot numbers leave zero as the unset column hint.
        if (last != 0 && text.SequenceEqual(entries[last - 1].Value.AsSpan()))
            return entries[last - 1].Value;

        return GetOrAdd(text, entries, ref last);
    }

    private static string GetOrAdd(ReadOnlySpan<char> text, Entry[] entries, ref int last)
    {
        var hash = string.GetHashCode(text, StringComparison.Ordinal);
        var bucket = (hash & (BucketCount - 1)) * EntriesPerBucket;
        for (int i = bucket; i < bucket + EntriesPerBucket; i++)
        {
            ref var entry = ref entries[i];
            if (entry.Value is null)
            {
                entry = new(hash, text.ToString());
            }
            else if (entry.Hash != hash || !text.SequenceEqual(entry.Value.AsSpan()))
            {
                continue;
            }

            last = i + 1;
            return entry.Value;
        }

        return text.ToString();
    }

    private readonly record struct Entry(int Hash, string Value);
}
