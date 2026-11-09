using System.Text;

namespace PocketCsvReader.Json;

// Frames one value at a time. Each frame is borrowed until the next input operation.
internal sealed class BufferedJsonInput : IDisposable
{
    private readonly StreamReader _text;
    private readonly BufferedRecordReader _reader;
    private readonly JsonValueFramer _framer = new(allowComments: false, closeObject: true);
    private readonly bool _asynchronous;
    private readonly Encoding? _positionEncoding;
    private readonly int _delimiterBytes;
    private int _delimiter = -1;

    public BufferedJsonInput(Stream stream, bool leaveOpen, bool asynchronous, Encoding? encoding = null, long? byteOffset = null)
    {
        _text = new StreamReader(stream, encoding ?? new UTF8Encoding(false, true), encoding is null, 4096, leaveOpen);
        _reader = new BufferedRecordReader(_text, 4096);
        _asynchronous = asynchronous;
        _positionEncoding = byteOffset.HasValue ? encoding : null;
        _delimiterBytes = _positionEncoding?.GetByteCount(" ") ?? 0;
        BytePosition = byteOffset.GetValueOrDefault();
    }

    public long Position => _reader.Position;
    public long BytePosition { get; private set; }
    public long LastValueByteStart { get; private set; }
    public int LastValueByteLength { get; private set; }

    public async ValueTask<bool> TryTakeAsync(char expected, CancellationToken token)
    {
        if (await PeekSignificantAsync(token).ConfigureAwait(false) != expected)
            return false;
        await TakeAsync(token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask ExpectAsync(char expected, CancellationToken token)
    {
        if (!await TryTakeAsync(expected, token).ConfigureAwait(false))
            throw new InvalidDataException($"Expected '{expected}' at position {Position}.");
    }

    public async ValueTask<int> PeekSignificantAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_delimiter >= 0)
            return _delimiter;
        while (true)
        {
            var next = _asynchronous
                ? await _reader.PeekAsync(token).ConfigureAwait(false)
                : _reader.Peek();
            if (next is not (' ' or '\t' or '\r' or '\n'))
                return next;
            await TakeAsync(token).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> MoveNextAsync(char close, bool subsequent, CancellationToken token)
    {
        if (await TryTakeAsync(close, token).ConfigureAwait(false))
            return false;
        if (subsequent)
            await ExpectAsync(',', token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<string> ReadPropertyNameAsync(JsonFieldBuffer scratch, CancellationToken token)
    {
        if (await PeekSignificantAsync(token).ConfigureAwait(false) != '"')
            throw new InvalidDataException($"Expected a JSON property name at position {Position}.");
        var label = await ReadValueAsync(token).ConfigureAwait(false);
        var name = JsonRecordParser.DecodeString(label.Span, scratch);
        await ExpectAsync(':', token).ConfigureAwait(false);
        return name;
    }

    public async ValueTask<ReadOnlyMemory<char>> ReadValueAsync(CancellationToken token)
    {
        var next = await PeekSignificantAsync(token).ConfigureAwait(false);
        if (next is -1 or '}' or ']' or ',' or ':')
            throw new InvalidDataException($"Expected a JSON value at position {Position}.");

        _reader.BeginOperation();
        LastValueByteStart = BytePosition;
        _framer.Prepare((char)next, Position);
        var frame = _asynchronous
            ? await _reader.ReadFrameAsync(_framer, token).ConfigureAwait(false)
            : _reader.ReadFrame(_framer);
        var value = frame?.Memory ?? throw new InvalidDataException($"Expected a JSON value at position {Position}.");
        // Scalar framing consumes its terminating character. Pending punctuation
        // is counted here, so consuming it from _delimiter cannot count it twice.
        _delimiter = _framer.Delimiter is '}' or ']' or ',' ? _framer.Delimiter : -1;
        if (_positionEncoding is not null)
        {
            LastValueByteLength = _positionEncoding.GetByteCount(value.Span);
            BytePosition += (long)LastValueByteLength + (_framer.Delimiter >= 0 ? _delimiterBytes : 0);
        }
        return value;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _text.Dispose();
    }

    private ValueTask<int> TakeAsync(CancellationToken token)
    {
        if (_delimiter >= 0)
        {
            var delimiter = _delimiter;
            _delimiter = -1;
            return ValueTask.FromResult(delimiter);
        }

        // Callers have already peeked a whitespace or punctuation character.
        BytePosition += _delimiterBytes;
        return _asynchronous ? _reader.ReadAsync(token) : ValueTask.FromResult(_reader.Read());
    }
}
