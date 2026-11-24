using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

// Indexes complete plain rows within one input buffer. The source owns the bytes
// and advances the index once per row. Both views expire on the next read.
internal sealed class Utf8BlockScanner : IDisposable
{
    private const int MinimumBufferSize = 1024;
    private const int FieldCapacity = 4096;
    private const int RowCapacity = 512;
    private readonly bool _useVector256 = Vector256.IsHardwareAccelerated;
    private readonly byte _delimiter;
    private readonly byte _terminator;
    private readonly byte _quote;
    private readonly int _terminatorLength;
    private int[]? _ends;
    private int[]? _rows;
    private int _rowCount;
    private int _rowIndex;
    private int _firstField;
    private int _fallbackAt = -1;

    private Utf8BlockScanner(DialectDescriptor dialect)
    {
        _delimiter = (byte)dialect.Delimiter;
        _terminator = (byte)dialect.LineTerminator[0];
        _quote = (byte)(dialect.QuoteChar ?? (char)_terminator);
        _terminatorLength = dialect.LineTerminator.Length;
    }

    public static Utf8BlockScanner? Create(DialectDescriptor dialect, int bufferSize)
        => bufferSize >= MinimumBufferSize
            && Vector128.IsHardwareAccelerated
            && dialect.LineTerminator is "\r\n" or "\n"
            && dialect.Delimiter is not ('\r' or '\n')
            && dialect.QuoteChar is not ('\r' or '\n')
            && dialect.Delimiter != dialect.QuoteChar
            && dialect.EscapeChar is null && dialect.CommentChar is null && !dialect.SkipInitialSpace
            ? new(dialect) : null;

    public void Reset()
    {
        _rowCount = _rowIndex = _firstField = 0;
        _fallbackAt = -1;
    }

    public bool TryRead(ReadOnlyMemory<byte> input, ref int position, Utf8FieldBuffer decodedFields, out CurrentRecord record)
    {
        if (_rowIndex == _rowCount && !ScanBatch(input.Span, position))
        {
            record = default;
            return false;
        }

        var ends = _ends!;
        var lastField = _rows![_rowIndex++];
        var count = lastField - _firstField;
        var length = ends[lastField - 1] + _terminatorLength;
        decodedFields.Reset(length, count);
        record = new(input.Slice(position, length), ends, _firstField, count, decodedFields);
        _firstField = lastField;
        position += length;
        return true;
    }

    private bool ScanBatch(ReadOnlySpan<byte> input, int position)
    {
        var blockSize = _useVector256 ? Vector256<byte>.Count : Vector128<byte>.Count;
        if (position == _fallbackAt || input.Length - position < blockSize)
            return false;

        var ends = _ends ??= ArrayPool<int>.Shared.Rent(FieldCapacity);
        var rows = _rows ??= ArrayPool<int>.Shared.Rent(RowCapacity);
        var rowStart = position;
        var fieldCount = 0;
        var rowCount = 0;
        var block = position & -blockSize;
        var remaining = uint.MaxValue << (position - block);
        while (block <= input.Length - blockSize)
        {
            // One block can contribute at most blockSize offsets and rows.
            if (fieldCount > FieldCapacity - blockSize || rowCount > RowCapacity - blockSize)
            {
                if (rowCount == 0)
                    _fallbackAt = rowStart;
                break;
            }

            LoadMasks(input, block, out var delimiters, out var terminators, out var quotes);
            quotes &= remaining;
            if (quotes != 0)
                remaining &= (1u << BitOperations.TrailingZeroCount(quotes)) - 1;

            var boundaries = (delimiters | terminators) & remaining;
            while (boundaries != 0)
            {
                var bit = BitOperations.TrailingZeroCount(boundaries);
                var end = block + bit;
                ends[fieldCount++] = end - rowStart;
                if ((terminators & (1u << bit)) != 0)
                {
                    if (_terminatorLength == 2 && (end + 1 == input.Length || input[end + 1] != (byte)'\n'))
                    {
                        _fallbackAt = rowStart;
                        goto Publish;
                    }
                    rows[rowCount++] = fieldCount;
                    rowStart = end + _terminatorLength;
                }

                boundaries &= boundaries - 1;
            }

            if (quotes != 0)
            {
                _fallbackAt = rowStart;
                break;
            }

            block += blockSize;
            remaining = uint.MaxValue;
        }

    Publish:
        _firstField = _rowIndex = 0;
        _rowCount = rowCount;
        return rowCount != 0;
    }

    private void LoadMasks(ReadOnlySpan<byte> input, int block, out uint delimiters, out uint terminators, out uint quotes)
    {
        ref var start = ref MemoryMarshal.GetReference(input);
        if (_useVector256)
        {
            var bytes = Vector256.LoadUnsafe(ref start, (nuint)block);
            delimiters = Vector256.Equals(bytes, Vector256.Create(_delimiter)).ExtractMostSignificantBits();
            terminators = Vector256.Equals(bytes, Vector256.Create(_terminator)).ExtractMostSignificantBits();
            quotes = _quote == _terminator ? 0 : Vector256.Equals(bytes, Vector256.Create(_quote)).ExtractMostSignificantBits();
        }
        else
        {
            var bytes = Vector128.LoadUnsafe(ref start, (nuint)block);
            delimiters = Vector128.Equals(bytes, Vector128.Create(_delimiter)).ExtractMostSignificantBits();
            terminators = Vector128.Equals(bytes, Vector128.Create(_terminator)).ExtractMostSignificantBits();
            quotes = _quote == _terminator ? 0 : Vector128.Equals(bytes, Vector128.Create(_quote)).ExtractMostSignificantBits();
        }
    }

    public void Dispose()
    {
        if (_ends is not null) ArrayPool<int>.Shared.Return(_ends);
        if (_rows is not null) ArrayPool<int>.Shared.Return(_rows);
    }
}
