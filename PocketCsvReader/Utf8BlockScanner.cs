using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;

internal sealed class Utf8BlockScanner
{
    private const int MaxScanBytes = 1024;

    // Wider rows can use the general scanner without growing this scratch buffer.
    private readonly int[] _ends = new int[32];
    private readonly int _blockSize = Vector256.IsHardwareAccelerated ? Vector256<byte>.Count : Vector128<byte>.Count;
    private readonly byte _delimiter;
    private readonly byte _terminator;
    private readonly byte _quote;
    private readonly bool _crlf;
    private int _blockStart = -1;
    private int _minimumAvailable;
    private uint _delimiters;
    private uint _specials;

    private Utf8BlockScanner(DialectDescriptor dialect)
    {
        _delimiter = (byte)dialect.Delimiter;
        _terminator = (byte)dialect.LineTerminator[0];
        _quote = (byte)(dialect.QuoteChar ?? (char)_terminator);
        _crlf = dialect.LineTerminator.Length == 2;
    }

    public static Utf8BlockScanner? Create(DialectDescriptor dialect, int bufferSize)
        => bufferSize >= MaxScanBytes
            && Vector128.IsHardwareAccelerated
            && dialect.LineTerminator is "\r\n" or "\n"
            && dialect.Delimiter is not ('\r' or '\n')
            && dialect.QuoteChar is not ('\r' or '\n')
            && dialect.Delimiter != dialect.QuoteChar
            && dialect.EscapeChar is null && dialect.CommentChar is null && !dialect.SkipInitialSpace
            ? new(dialect) : null;

    public void Reset() => _blockStart = -1;

    // Avoid rescanning similarly sized rows after a refill or a long-record fallback.
    public bool CanScan(int available) => available >= _minimumAvailable;

    public void ObserveFallback(int recordLength)
        => _minimumAvailable = recordLength <= MaxScanBytes ? recordLength : int.MaxValue;

    // A zero count asks the general scanner to restart at rowStart. This includes
    // quoted fields, incomplete records and a short final block at the end of a buffer.
    public int Scan(ReadOnlySpan<byte> input, int rowStart, out int nextRow)
    {
        var count = 0;
        nextRow = rowStart;
        var limit = rowStart + Math.Min(input.Length - rowStart, MaxScanBytes);
        for (var position = rowStart; position < limit;)
        {
            var block = position & -_blockSize;
            if (block != _blockStart)
            {
                if (input.Length - block < _blockSize)
                    break;

                LoadMasks(input, block);
                _blockStart = block;
            }

            // Reuse the last block across rows. Only delimiters before the first quote
            // or terminator belong to the candidate record.
            var remaining = uint.MaxValue << (position - block);
            var special = BitOperations.TrailingZeroCount(_specials & remaining);
            var delimiters = _delimiters & remaining;
            if (special != 32)
                delimiters &= (1u << special) - 1;

            if (count + BitOperations.PopCount(delimiters) >= _ends.Length)
                return 0;

            while (delimiters != 0)
            {
                _ends[count++] = block + BitOperations.TrailingZeroCount(delimiters) - rowStart;
                delimiters &= delimiters - 1;
            }

            if (special != 32)
            {
                var end = block + special;
                if (input[end] != _terminator || (_crlf && (end + 1 == input.Length || input[end + 1] != (byte)'\n')))
                    return 0;

                _ends[count++] = end - rowStart;
                nextRow = end + (_crlf ? 2 : 1);
                return count;
            }

            position = block + _blockSize;
        }

        return 0;
    }

    public void WriteFields(Span<FieldSpan> fields)
    {
        var start = 0;
        for (var i = 0; i < fields.Length; i++)
        {
            var end = _ends[i];
            var length = end - start;
            fields[i] = new(new SpanInfo(length == 0 ? 0 : start, length, IsStarted: length != 0, IsComplete: true), default);
            start = end + 1;
        }
    }

    private void LoadMasks(ReadOnlySpan<byte> input, int block)
    {
        ref var start = ref MemoryMarshal.GetReference(input);
        if (Vector256.IsHardwareAccelerated)
        {
            var bytes = Vector256.LoadUnsafe(ref start, (nuint)block);
            _delimiters = Vector256.Equals(bytes, Vector256.Create(_delimiter)).ExtractMostSignificantBits();
            _specials = (Vector256.Equals(bytes, Vector256.Create(_terminator))
                | Vector256.Equals(bytes, Vector256.Create(_quote))).ExtractMostSignificantBits();
        }
        else
        {
            var bytes = Vector128.LoadUnsafe(ref start, (nuint)block);
            _delimiters = Vector128.Equals(bytes, Vector128.Create(_delimiter)).ExtractMostSignificantBits();
            _specials = (Vector128.Equals(bytes, Vector128.Create(_terminator))
                | Vector128.Equals(bytes, Vector128.Create(_quote))).ExtractMostSignificantBits();
        }
    }
}
