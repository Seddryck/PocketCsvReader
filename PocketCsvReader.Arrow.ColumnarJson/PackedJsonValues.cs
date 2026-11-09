using System.Buffers;

namespace PocketCsvReader.Arrow.ColumnarJson;

// Holds byte lengths and gaps relative to the end of the preceding value.
// Small lengths and gaps occupy one byte. The record owns the packed buffer.
internal sealed class PackedJsonValues(long start, int capacity)
{
    private readonly ArrayBufferWriter<byte> _boundaries = capacity == 0 ? new() : new(capacity);
    private readonly long _start = start;
    private long _end = start;
    private int _maxBytes;
    private int _maxChars;

    public int Count { get; private set; }

    public void Add(long offset, int length, int charLength)
    {
        var gap = (ulong)(offset - _end);
        var entry = ((ulong)(length - 1) << 2) | Math.Min(3UL, gap);
        if (entry < 128 && gap < 3)
        {
            _boundaries.GetSpan(1)[0] = (byte)entry;
            _boundaries.Advance(1);
        }
        else
        {
            WriteVarint(entry);
            if (gap >= 3)
                WriteVarint(gap - 3);
        }

        _end = offset + length;
        _maxBytes = Math.Max(_maxBytes, length);
        _maxChars = Math.Max(_maxChars, charLength);
        Count = checked(Count + 1);
    }

    public Reader Open(SharedJsonFile file) => new(this, file, _start);

    private void WriteVarint(ulong value)
    {
        var bytes = _boundaries.GetSpan(10);
        var position = 0;
        while (value >= 128)
        {
            bytes[position++] = (byte)(value | 128);
            value >>= 7;
        }
        bytes[position++] = (byte)value;
        _boundaries.Advance(position);
    }

    internal sealed class Reader(PackedJsonValues index, SharedJsonFile file, long start) : IDisposable
    {
        private byte[]? _bytes;
        private char[]? _chars;
        private long _offset = start;
        private long _windowOffset;
        private int _buffered;
        private int _position;
        private int _charCount;

        // Borrowed until the next read or disposal.
        public ReadOnlySpan<char> Value => _chars.AsSpan(0, _charCount);

        public bool MoveNext()
        {
            var boundaries = index._boundaries.WrittenSpan;
            if (_position == boundaries.Length)
                return false;

            var entry = ReadVarint(boundaries);
            var gap = entry & 3;
            if (gap == 3)
                gap += ReadVarint(boundaries);
            _offset += (long)gap;
            var length = (int)((entry >> 2) + 1);

            _bytes ??= ArrayPool<byte>.Shared.Rent(Math.Max(16_384, index._maxBytes));
            _chars ??= ArrayPool<char>.Shared.Rent(index._maxChars);
            if (_offset + length > _windowOffset + _buffered)
                Refill(_bytes, length);
            var bytes = _bytes.AsSpan((int)(_offset - _windowOffset), length);
            _charCount = file.Encoding.GetChars(bytes, _chars);
            _offset += length;
            return true;
        }

        public void Dispose()
        {
            if (_chars is not null)
            {
                ArrayPool<char>.Shared.Return(_chars);
                _chars = null;
            }
            if (_bytes is not null)
            {
                ArrayPool<byte>.Shared.Return(_bytes);
                _bytes = null;
            }
        }

        private ulong ReadVarint(ReadOnlySpan<byte> bytes)
        {
            ulong value = bytes[_position++];
            if (value < 128)
                return value;

            value &= 127;
            for (var shift = 7; shift < 64; shift += 7)
            {
                var next = bytes[_position++];
                value |= (ulong)(next & 127) << shift;
                if (next < 128)
                    return value;
            }
            throw new InvalidDataException("The packed JSON boundary is invalid.");
        }

        private void Refill(byte[] bytes, int length)
        {
            _windowOffset = _offset;
            var buffer = bytes.AsSpan(0, (int)Math.Min(bytes.Length, index._end - _offset));
            _buffered = 0;
            while (_buffered < length)
            {
                var read = file.Read(_windowOffset + _buffered, buffer[_buffered..]);
                if (read == 0)
                    throw new EndOfStreamException("The indexed JSON value is incomplete.");
                _buffered += read;
            }
        }
    }
}
