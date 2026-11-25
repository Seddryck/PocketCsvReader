using System.Text;

namespace PocketCsvReader;

internal sealed class Utf8StringPool
{
    private const int BucketCount = 4096;
    private const int EntriesPerBucket = 4;
    private const int MaxFieldBytes = 128;
    private const int MaxHintedColumns = BucketCount * EntriesPerBucket;

    private readonly Entry[] _entries = new Entry[BucketCount * EntriesPerBucket];
    // Slots are stored one-based so zero represents a column without a recent entry.
    private int[] _lastByColumn = new int[32];

    public string GetString(int column, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;
        if (bytes.Length > MaxFieldBytes)
            return Encoding.UTF8.GetString(bytes);

        var lastByColumn = _lastByColumn;
        return (uint)column < (uint)lastByColumn.Length
            ? GetWithHint(bytes, ref lastByColumn[column])
            : GetWideColumn(column, bytes);
    }

    private string GetWithHint(ReadOnlySpan<byte> bytes, ref int last)
    {
        if (last != 0)
        {
            ref var previous = ref _entries[last - 1];
            if (bytes.SequenceEqual(previous.Bytes))
                return previous.Value;
        }

        return GetOrAdd(bytes, ref last);
    }

    private string GetWideColumn(int column, ReadOnlySpan<byte> bytes)
    {
        if ((uint)column >= MaxHintedColumns)
        {
            int ignored = 0;
            return GetOrAdd(bytes, ref ignored);
        }

        int capacity = Math.Min(MaxHintedColumns, Math.Max(_lastByColumn.Length * 2, column + 1));
        Array.Resize(ref _lastByColumn, capacity);
        return GetWithHint(bytes, ref _lastByColumn[column]);
    }

    private string GetOrAdd(ReadOnlySpan<byte> bytes, ref int last)
    {
        HashCode hash = default;
        hash.AddBytes(bytes);
        var code = hash.ToHashCode();
        var bucket = (code & (BucketCount - 1)) * EntriesPerBucket;

        for (int i = bucket; i < bucket + EntriesPerBucket; i++)
        {
            ref var entry = ref _entries[i];
            if (entry.Bytes is null)
            {
                // Input bytes are borrowed from a buffer reused on the next read.
                entry = new Entry(code, bytes.ToArray(), Encoding.UTF8.GetString(bytes));
            }
            else if (entry.Hash != code || !bytes.SequenceEqual(entry.Bytes))
            {
                continue;
            }

            last = i + 1;
            return entry.Value;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private readonly record struct Entry(int Hash, byte[] Bytes, string Value);
}
