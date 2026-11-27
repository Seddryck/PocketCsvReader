using System.Buffers;
using System.Text;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;

// Shared by successive records from one source. Like the source's byte buffer,
// these views remain valid only until the next read or disposal.
internal sealed class Utf8FieldBuffer : IDisposable
{
    public FieldMetadataBuffer PublishedFields { get; } = new();
    private char[]? _characters;
    private byte[]? _unescaped;
    private int[]? _lengths;
    private int _byteLength;
    private int _fieldCount;

    public void Reset(int byteLength, int fieldCount)
    {
        _byteLength = byteLength;
        _fieldCount = fieldCount;
        if (_lengths is not null)
            _lengths.AsSpan(0, Math.Min(fieldCount, _lengths.Length)).Fill(-1);
    }

    public ReadOnlyMemory<char> Decode(ReadOnlySpan<byte> record, SpanInfo field, int ordinal)
    {
        // A UTF-8 field, including replacement fallback, produces at most as
        // many UTF-16 code units as input bytes. Byte offsets therefore reserve
        // non-overlapping character slots without allocating per-field strings.
        if (_characters is null || _characters.Length < _byteLength)
        {
            if (_characters is not null) ArrayPool<char>.Shared.Return(_characters);
            _characters = ArrayPool<char>.Shared.Rent(_byteLength);
        }
        if (_lengths is null || _lengths.Length < _fieldCount)
        {
            if (_lengths is not null) ArrayPool<int>.Shared.Return(_lengths);
            _lengths = ArrayPool<int>.Shared.Rent(_fieldCount);
            _lengths.AsSpan(0, _fieldCount).Fill(-1);
        }
        int length = _lengths[ordinal];
        if (length < 0)
            _lengths[ordinal] = length = Encoding.UTF8.GetChars(record.Slice(field.Start, field.Length), _characters.AsSpan(field.Start, field.Length));
        return _characters.AsMemory(field.Start, length);
    }

    public ReadOnlySpan<byte> Unescape(ReadOnlySpan<byte> value, FieldEscaper escaper)
    {
        // Only one escaped UTF-8 view is borrowed at a time.
        if (_unescaped is null || _unescaped.Length < value.Length)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(value.Length);
            if (_unescaped is not null) ArrayPool<byte>.Shared.Return(_unescaped);
            _unescaped = buffer;
        }
        return _unescaped.AsSpan(0, escaper.UnescapeUtf8(value, _unescaped));
    }

    public void Dispose()
    {
        if (_characters is not null) ArrayPool<char>.Shared.Return(_characters);
        if (_lengths is not null) ArrayPool<int>.Shared.Return(_lengths);
        if (_unescaped is not null) ArrayPool<byte>.Shared.Return(_unescaped);
        _characters = null;
        _lengths = null;
        _unescaped = null;
    }
}
