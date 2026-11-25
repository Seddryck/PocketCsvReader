using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

// Indexes complete plain rows within one input buffer. The source owns the input
// and advances the index once per row. Both views expire on the next read.
internal sealed class CsvBlockScanner<T> : IDisposable
    where T : unmanaged, IBinaryInteger<T>
{
    private const int MinimumBufferSize = 1024;
    private const int FieldCapacity = 4096;
    private const int RowCapacity = 512;
    private readonly bool _useVector256 = Vector256.IsHardwareAccelerated;
    private readonly T _delimiter;
    private readonly T _terminator;
    private readonly T _quote;
    private readonly int _terminatorLength;
    private int[]? _ends;
    private int[]? _rows;
    private int _rowCount;
    private int _rowIndex;
    private int _firstField;
    // Store the remaining length so callers can pass either a full buffer or a suffix.
    private int _fallbackRemaining = -1;

    private CsvBlockScanner(DialectDescriptor dialect)
    {
        _delimiter = T.CreateChecked((ushort)dialect.Delimiter);
        _terminator = T.CreateChecked((ushort)dialect.LineTerminator[0]);
        _quote = T.CreateChecked((ushort)(dialect.QuoteChar ?? dialect.LineTerminator[0]));
        _terminatorLength = dialect.LineTerminator.Length;
    }

    public static CsvBlockScanner<T>? Create(DialectDescriptor dialect, int bufferSize)
        => bufferSize >= MinimumBufferSize
            && Vector128.IsHardwareAccelerated
            && dialect.LineTerminator is "\r\n" or "\n"
            && dialect.Delimiter is not ('\r' or '\n')
            && dialect.QuoteChar is not ('\r' or '\n')
            && dialect.Delimiter != dialect.QuoteChar
            && UsesPlainFields(dialect)
            ? new(dialect) : null;

    private static bool UsesPlainFields(DialectDescriptor dialect)
        => dialect.EscapeChar is null && dialect.CommentChar is null && !dialect.SkipInitialSpace
            && dialect.ArrayDelimiter is null && dialect.ArrayPrefix is null && dialect.ArraySuffix is null;

    public void Reset()
    {
        _rowCount = _rowIndex = _firstField = 0;
        _fallbackRemaining = -1;
    }

    public bool TryRead(ReadOnlySpan<T> input, int position, out CsvRowOffsets row)
    {
        if (_rowIndex == _rowCount && !ScanBatch(input, position))
        {
            row = default;
            return false;
        }

        var ends = _ends!;
        var lastField = _rows![_rowIndex++];
        row = new(ends, _firstField, lastField - _firstField, ends[lastField - 1] + _terminatorLength);
        _firstField = lastField;
        return true;
    }

    private bool ScanBatch(ReadOnlySpan<T> input, int position)
    {
        var blockSize = _useVector256 ? Vector256<T>.Count : Vector128<T>.Count;
        if (input.Length - position == _fallbackRemaining || input.Length - position < blockSize)
            return false;

        var ends = _ends ??= ArrayPool<int>.Shared.Rent(FieldCapacity);
        var rows = _rows ??= ArrayPool<int>.Shared.Rent(RowCapacity);
        var index = new BatchIndex { RowStart = position };
        var block = position & -blockSize;
        var remaining = uint.MaxValue << (position - block);
        for (; block <= input.Length - blockSize; block += blockSize)
        {
            // One block can contribute at most blockSize offsets and rows.
            if (index.FieldCount > FieldCapacity - blockSize || index.RowCount > RowCapacity - blockSize)
            {
                if (index.RowCount == 0)
                    _fallbackRemaining = input.Length - index.RowStart;
                break;
            }

            if (!TryIndexBlock(input, block, remaining, ends, rows, ref index))
            {
                _fallbackRemaining = input.Length - index.RowStart;
                break;
            }

            remaining = uint.MaxValue;
        }

        _firstField = _rowIndex = 0;
        _rowCount = index.RowCount;
        return _rowCount != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryIndexBlock(ReadOnlySpan<T> input, int block, uint remaining, int[] ends, int[] rows, ref BatchIndex index)
    {
        LoadMasks(input, block, out var delimiters, out var terminators, out var quotes);
        quotes &= remaining;
        if (quotes != 0)
            remaining &= (1u << BitOperations.TrailingZeroCount(quotes)) - 1;

        var boundaries = (delimiters | terminators) & remaining;
        while (boundaries != 0)
        {
            var bit = BitOperations.TrailingZeroCount(boundaries);
            var end = block + bit;
            ends[index.FieldCount++] = end - index.RowStart;
            if ((terminators & (1u << bit)) != 0)
            {
                if (_terminatorLength == 2 && (end + 1 == input.Length || input[end + 1] != T.CreateChecked((ushort)'\n')))
                    return false;

                rows[index.RowCount++] = index.FieldCount;
                index.RowStart = end + _terminatorLength;
            }

            boundaries &= boundaries - 1;
        }

        return quotes == 0;
    }

    private struct BatchIndex
    {
        public int RowStart;
        public int FieldCount;
        public int RowCount;
    }

    private void LoadMasks(ReadOnlySpan<T> input, int block, out uint delimiters, out uint terminators, out uint quotes)
    {
        ref var start = ref MemoryMarshal.GetReference(input);
        if (_useVector256)
        {
            var values = Vector256.LoadUnsafe(ref start, (nuint)block);
            delimiters = Vector256.Equals(values, Vector256.Create(_delimiter)).ExtractMostSignificantBits();
            terminators = Vector256.Equals(values, Vector256.Create(_terminator)).ExtractMostSignificantBits();
            quotes = _quote == _terminator ? 0 : Vector256.Equals(values, Vector256.Create(_quote)).ExtractMostSignificantBits();
        }
        else
        {
            var values = Vector128.LoadUnsafe(ref start, (nuint)block);
            delimiters = Vector128.Equals(values, Vector128.Create(_delimiter)).ExtractMostSignificantBits();
            terminators = Vector128.Equals(values, Vector128.Create(_terminator)).ExtractMostSignificantBits();
            quotes = _quote == _terminator ? 0 : Vector128.Equals(values, Vector128.Create(_quote)).ExtractMostSignificantBits();
        }
    }

    public void Dispose()
    {
        if (_ends is not null) ArrayPool<int>.Shared.Return(_ends);
        if (_rows is not null) ArrayPool<int>.Shared.Return(_rows);
    }
}

internal readonly record struct CsvRowOffsets(int[] Ends, int FirstField, int FieldCount, int Length);
