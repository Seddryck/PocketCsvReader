using System.Text;

namespace PocketCsvReader;

internal sealed class Utf8StringPool
{
    private const int BucketCount = 4096;
    private const int EntriesPerBucket = 4;
    private const int MaxFieldBytes = 128;

    private readonly Entry[] _entries = new Entry[BucketCount * EntriesPerBucket];

    public string GetString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;
        if (bytes.Length > MaxFieldBytes)
            return Encoding.UTF8.GetString(bytes);

        HashCode hash = default;
        hash.AddBytes(bytes);
        var code = hash.ToHashCode();
        var bucket = (code & (BucketCount - 1)) * EntriesPerBucket;

        for (int i = bucket; i < bucket + EntriesPerBucket; i++)
        {
            ref var entry = ref _entries[i];
            if (entry.Bytes is null)
            {
                var value = Encoding.UTF8.GetString(bytes);
                // Input bytes are borrowed from a buffer reused on the next read.
                entry = new Entry(code, bytes.ToArray(), value);
                return value;
            }

            if (entry.Hash == code && bytes.SequenceEqual(entry.Bytes))
                return entry.Value;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private readonly record struct Entry(int Hash, byte[] Bytes, string Value);
}
