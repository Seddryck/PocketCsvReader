using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

// Indexes complete rows within one input buffer. The source owns the input
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
    private readonly bool _doubleQuote;
    private int[]? _ends;
    private int[]? _rows;
    private int _rowCount;
    private int _rowIndex;
    // Store the remaining length so callers can pass either a full buffer or a suffix.
    private int _fallbackRemaining = -1;

    private CsvBlockScanner(DialectDescriptor dialect)
    {
        _delimiter = T.CreateChecked((ushort)dialect.Delimiter);
        _terminator = T.CreateChecked((ushort)dialect.LineTerminator[0]);
        _quote = T.CreateChecked((ushort)(dialect.QuoteChar ?? dialect.LineTerminator[0]));
        _terminatorLength = dialect.LineTerminator.Length;
        _doubleQuote = dialect.DoubleQuote;
    }

    public static CsvBlockScanner<T>? Create(DialectDescriptor dialect, int bufferSize)
        => bufferSize is >= MinimumBufferSize and <= CsvFieldOffset.OffsetMask
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
        _rowCount = _rowIndex = 0;
        _fallbackRemaining = -1;
    }

    public bool TryRead(ReadOnlySpan<T> input, int position, out CsvRowOffsets row)
    {
        var count = Advance(input, ref position);
        row = count == 0 ? default : GetCurrentOffsets();
        return count != 0;
    }

    public int Advance(ReadOnlySpan<T> input, ref int position)
        => _rowIndex != _rowCount || ScanBatch(input, position) ? AdvanceBuffered(ref position) : 0;

    public int AdvanceBuffered(ref int position)
    {
        var index = _rowIndex;
        if (index == _rowCount)
            return 0;

        var rows = _rows!;
        var lastField = rows[index];
        position += GetRowLength(_ends!, lastField);
        _rowIndex = index + 1;
        return lastField - FirstField(rows, index);
    }

    public CsvRowOffsets GetCurrentOffsets()
    {
        var index = _rowIndex - 1;
        var rows = _rows!;
        var firstField = FirstField(rows, index);
        var lastField = rows[index];
        var ends = _ends!;
        return new(ends, firstField, lastField - firstField, GetRowLength(ends, lastField));
    }

    private static int FirstField(int[] rows, int index) => index == 0 ? 0 : rows[index - 1];

    private int GetRowLength(int[] ends, int lastField)
        => (ends[lastField - 1] & CsvFieldOffset.OffsetMask) + _terminatorLength;

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
        while (block <= input.Length - blockSize)
        {
            // One block can contribute at most blockSize offsets and rows.
            if (index.FieldCount > FieldCapacity - blockSize || index.RowCount > RowCapacity - blockSize)
            {
                if (index.RowCount == 0)
                    _fallbackRemaining = input.Length - index.RowStart;
                break;
            }

            if (!TryIndexBlock(input, block, remaining, ends, rows, ref index, out var opening)
                || !TryAdvance(input, blockSize, ends, rows, ref index, opening, ref block, ref remaining))
            {
                _fallbackRemaining = input.Length - index.RowStart;
                break;
            }
        }

        _rowIndex = 0;
        _rowCount = index.RowCount;
        return _rowCount != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryIndexBlock(ReadOnlySpan<T> input, int block, uint remaining, int[] ends, int[] rows, ref BatchIndex index, out int opening)
    {
        LoadMasks(input, block, out var delimiters, out var terminators, out var quotes);
        quotes &= remaining;
        opening = -1;
        if (quotes != 0)
        {
            var bit = BitOperations.TrailingZeroCount(quotes);
            opening = block + bit;
            remaining &= (1u << bit) - 1;
        }

        var boundaries = (delimiters | terminators) & remaining;
        while (boundaries != 0)
        {
            var bit = BitOperations.TrailingZeroCount(boundaries);
            var end = block + bit;
            ends[index.FieldCount++] = end - index.RowStart;
            if ((terminators & (1u << bit)) != 0)
            {
                if (!HasRecordTerminator(input, end))
                    return false;

                rows[index.RowCount++] = index.FieldCount;
                index.RowStart = end + _terminatorLength;
            }

            boundaries &= boundaries - 1;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAdvance(ReadOnlySpan<T> input, int blockSize, int[] ends, int[] rows,
        ref BatchIndex index, int opening, ref int block, ref uint remaining)
    {
        if (opening < 0)
        {
            block += blockSize;
            remaining = uint.MaxValue;
            return true;
        }

        // The decoded-character scanner continues to use its scalar quote parser.
        if (typeof(T) != typeof(byte) || !TryIndexQuotedField(input, opening, ends, rows, ref index, out var next))
            return false;

        block = next & -blockSize;
        remaining = uint.MaxValue << (next - block);
        return true;
    }

    private bool TryIndexQuotedField(ReadOnlySpan<T> input, int opening, int[] ends, int[] rows,
        ref BatchIndex index, out int next)
    {
        next = 0;
        var firstField = index.RowCount == 0 ? 0 : rows[index.RowCount - 1];
        var fieldStart = index.FieldCount == firstField ? index.RowStart
            : index.RowStart + (ends[index.FieldCount - 1] & CsvFieldOffset.OffsetMask) + 1;
        // Incomplete, malformed or ambiguous rows are reparsed from their start.
        if (opening != fieldStart || !TryFindQuoteBoundary(input, opening, out var end, out var flags))
            return false;

        ends[index.FieldCount++] = (end - index.RowStart) | flags;
        next = end + 1;
        if (input[end] == _terminator)
        {
            rows[index.RowCount++] = index.FieldCount;
            index.RowStart = next = end + _terminatorLength;
        }

        return true;
    }

    private bool TryFindQuoteBoundary(ReadOnlySpan<T> input, int opening, out int end, out int flags)
    {
        flags = CsvFieldOffset.Quoted;
        end = opening + 1;
        while (end < input.Length)
        {
            // Ignore delimiters and newlines inside the quoted field.
            var quote = input[end..].IndexOf(_quote);
            if (quote < 0)
                break;

            end += quote + 1;
            if (end == input.Length)
                break;

            var following = input[end];
            if (following != _quote || !_doubleQuote)
                return following == _delimiter || (following == _terminator && HasRecordTerminator(input, end));

            flags |= CsvFieldOffset.Escaped;
            end++;
        }

        return false;
    }

    private bool HasRecordTerminator(ReadOnlySpan<T> input, int end)
        => _terminatorLength == 1 || (end + 1 < input.Length && input[end + 1] == T.CreateChecked((ushort)'\n'));

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

internal static class CsvFieldOffset
{
    // The high bits preserve quote metadata alongside the raw field boundary.
    public const int OffsetMask = (1 << 30) - 1;
    public const int Quoted = 1 << 30;
    public const int Escaped = int.MinValue;
}
