using System.Buffers;
using System.Runtime.CompilerServices;

namespace PocketCsvReader;

// Rows use offsets into the batch's slabs because the producer may grow a slab
// before publishing the batch. After publication these buffers are read-only.
internal readonly record struct CsvBatchRecord(
    int DataStart, int DataLength, int FieldStart, int FieldCount, bool Utf8, bool Compact);

internal sealed class CsvBatchStorage : IDisposable
{
    internal const int StorageLimit = 256 * 1024;

    public CsvBatchBuffer<byte> Bytes;
    public CsvBatchBuffer<char> Characters;
    public CsvBatchBuffer<int> Offsets;
    public CsvBatchBuffer<FieldSpan> Fields;
    private long _nestedFieldBytes;

    public long UsedBytes => Bytes.Count + (2L * Characters.Count) + ((long)sizeof(int) * Offsets.Count)
        + ((long)Unsafe.SizeOf<FieldSpan>() * Fields.Count) + _nestedFieldBytes;

    public int AppendFields(ReadOnlySpan<FieldSpan> fields)
    {
        int start = Fields.Append(fields);
        foreach (ref var field in Fields.Slice(start, fields.Length))
        {
            if (field.Children is { Length: > 0 } children)
            {
                field.Children = FieldMetadataBuffer.CopyOwnedTree(children);
                _nestedFieldBytes += CountNestedFieldBytes(children);
            }
        }
        return start;
    }

    private static long CountNestedFieldBytes(FieldSpan[] fields)
    {
        long bytes = (long)fields.Length * Unsafe.SizeOf<FieldSpan>();
        foreach (var field in fields)
        {
            if (field.Children is { } children)
                bytes += CountNestedFieldBytes(children);
        }
        return bytes;
    }

    public CurrentRecord Borrow(CsvBatchRecord row, Utf8FieldBuffer decoder)
    {
        if (row.Utf8)
        {
            decoder.Reset(row.DataLength, row.FieldCount);
            var data = Bytes.Memory(row.DataStart, row.DataLength);
            if (row.Compact)
                return new(data, Offsets.Array, row.FieldStart, row.FieldCount, decoder);
            if (row.FieldCount == 0)
                return new(data, [], decoder);
            return new(data, Fields.Array, row.FieldStart, row.FieldCount, decoder);
        }

        var characters = Characters.Memory(row.DataStart, row.DataLength);
        if (row.Compact)
        {
            return new(characters, new CsvRowOffsets(Offsets.Array, row.FieldStart, row.FieldCount, row.DataLength),
                decoder.PublishedFields);
        }
        if (row.FieldCount == 0)
            return new(characters, []);
        return new(characters, Fields.Array, row.FieldStart, row.FieldCount, decoder.PublishedFields);
    }

    public void Dispose()
    {
        Bytes.Dispose();
        Characters.Dispose();
        Offsets.Dispose();
        Fields.Dispose();
    }
}

// Pool normal batches. Outlier rows larger than the batch budget get dedicated
// storage so they do not establish a long-lived pool size for later batches.
internal struct CsvBatchBuffer<T>
{
    private static readonly int PoolLimit = CsvBatchStorage.StorageLimit / Unsafe.SizeOf<T>();
    private T[]? _array;
    private bool _rented;

    public readonly T[] Array => _array ?? System.Array.Empty<T>();
    public int Count { get; private set; }

    public int Append(ReadOnlySpan<T> values)
    {
        int offset = Reserve(values.Length);
        values.CopyTo(Slice(offset, values.Length));
        return offset;
    }

    public int Reserve(int length)
    {
        int start = Count;
        int required = checked(start + length);
        if (required > (_array?.Length ?? 0))
            Grow(required);
        Count = required;
        return start;
    }

    public readonly Span<T> Slice(int start, int length) => _array.AsSpan(start, length);
    public readonly ReadOnlyMemory<T> Memory(int start, int length) => _array.AsMemory(start, length);

    private void Grow(int required)
    {
        bool rent = required <= PoolLimit;
        int suggested = _array is null
            ? (int)Math.Min(PoolLimit, 64L * required)
            : (int)Math.Min(PoolLimit, 2L * _array.Length);
        var next = rent ? ArrayPool<T>.Shared.Rent(Math.Max(required, suggested)) : new T[required];
        _array.AsSpan(0, Count).CopyTo(next);
        Return();
        _array = next;
        _rented = rent;
    }

    private readonly void Return()
    {
        if (_rented)
            ArrayPool<T>.Shared.Return(_array!, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    public void Dispose()
    {
        Return();
        this = default;
    }
}
