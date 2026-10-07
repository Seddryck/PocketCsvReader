using System.Buffers;

namespace PocketCsvReader;

internal interface IRecordFramer
{
    void Reset();
    FrameScanResult Scan(ReadOnlySpan<char> input);
    FrameScanResult CompleteAtEof();
}

internal readonly record struct FrameScanResult(int Consumed, bool Complete, int ContentLength = 0);

internal sealed class BufferedRecordReader : IDisposable
{
    private readonly StreamReader _reader;
    private readonly int _bufferSize;
    private readonly ArrayPool<char> _pool;
    private char[]? _buffer;
    private int _length;
    private int _index;
    private bool _eof;
    private bool _capturing;
    private int _captureStart;
    private List<BufferSegment>? _segments;
    private char[]? _ownedRecord;
    private char[]? _protectedBuffer;
    private CapturedRecord? _protectedCapture;

    public long Position { get; private set; }

    public BufferedRecordReader(StreamReader reader, int bufferSize, ArrayPool<char>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        _reader = reader;
        _bufferSize = bufferSize;
        _pool = pool ?? ArrayPool<char>.Shared;
    }

    public void BeginOperation()
    {
        _protectedCapture = null;
        Return(ref _ownedRecord);
        Return(ref _protectedBuffer);
    }

    public int Peek()
    {
        EnsureAvailable();
        return _index < _length ? _buffer![_index] : -1;
    }

    public async ValueTask<int> PeekAsync(CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return _index < _length ? _buffer![_index] : -1;
    }

    public int Read()
    {
        var current = Peek();
        if (current >= 0)
            Advance(1);
        return current;
    }

    public async ValueTask<int> ReadAsync(CancellationToken cancellationToken)
    {
        var current = await PeekAsync(cancellationToken).ConfigureAwait(false);
        if (current >= 0)
            Advance(1);
        return current;
    }

    public CapturedRecord? ReadFrame(IRecordFramer framer)
    {
        ArgumentNullException.ThrowIfNull(framer);
        BeginCapture();
        framer.Reset();

        while (true)
        {
            EnsureAvailable();
            if (_index == _length)
            {
                var eofResult = framer.CompleteAtEof();
                return eofResult.Complete ? EndCapture(eofResult.ContentLength) : CancelCapture();
            }

            var result = framer.Scan(_buffer.AsSpan(_index, _length - _index));
            ValidateScanResult(result);
            Advance(result.Consumed);
            if (result.Complete)
                return EndCapture(result.ContentLength);
        }
    }

    public async ValueTask<CapturedRecord?> ReadFrameAsync(
        IRecordFramer framer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(framer);
        BeginCapture();
        framer.Reset();

        while (true)
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (_index == _length)
            {
                var eofResult = framer.CompleteAtEof();
                return eofResult.Complete ? EndCapture(eofResult.ContentLength) : CancelCapture();
            }

            var result = framer.Scan(_buffer.AsSpan(_index, _length - _index));
            ValidateScanResult(result);
            Advance(result.Consumed);
            if (result.Complete)
                return EndCapture(result.ContentLength);
        }
    }

    public void Protect(CapturedRecord capture)
        => _protectedCapture = capture;

    private void BeginCapture()
    {
        if (_capturing)
            throw new InvalidOperationException("A record is already being captured.");

        _capturing = true;
        _captureStart = _index;
        _segments = null;
    }

    private CapturedRecord EndCapture(int contentLength)
    {
        var capturedLength = (_segments?.Sum(segment => segment.Length) ?? 0) + _index - _captureStart;
        if (contentLength < 0 || contentLength > capturedLength)
            throw new InvalidOperationException(
                $"The record framer returned content length {contentLength} for {capturedLength} captured characters.");

        CapturedRecord capture;
        if (contentLength == 0)
        {
            ReleaseSegments();
            capture = new CapturedRecord(ReadOnlyMemory<char>.Empty, null);
        }
        else if (_segments is null)
        {
            capture = new CapturedRecord(
                _buffer!.AsMemory(_captureStart, contentLength),
                _buffer);
        }
        else
        {
            capture = CoalesceCapture(contentLength);
        }

        _capturing = false;
        return capture;
    }

    private CapturedRecord CoalesceCapture(int contentLength)
    {
        var owned = _pool.Rent(contentLength);
        var destination = owned.AsSpan(0, contentLength);
        var copied = CopySegments(destination);

        if (copied < contentLength)
            _buffer!.AsSpan(_captureStart, contentLength - copied).CopyTo(destination[copied..]);

        ReleaseSegments();
        _ownedRecord = owned;
        return new CapturedRecord(owned.AsMemory(0, contentLength), owned);
    }

    private int CopySegments(Span<char> destination)
    {
        var copied = 0;
        foreach (var segment in _segments!)
        {
            var count = Math.Min(segment.Length, destination.Length - copied);
            if (count == 0)
                break;
            segment.Array.AsSpan(segment.Start, count).CopyTo(destination[copied..]);
            copied += count;
        }
        return copied;
    }

    private CapturedRecord? CancelCapture()
    {
        ReleaseSegments();
        _capturing = false;
        return null;
    }

    private void EnsureAvailable()
    {
        if (_index < _length || _eof)
            return;

        PrepareForRefill();
        var buffer = _pool.Rent(_bufferSize);
        var length = _reader.Read(buffer, 0, _bufferSize);
        if (length == 0)
        {
            _pool.Return(buffer);
            _eof = true;
            _buffer = null;
            _length = 0;
            _index = 0;
            return;
        }

        _buffer = buffer;
        _length = length;
        _index = 0;
        if (_capturing)
            _captureStart = 0;
    }

    private async ValueTask EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (_index < _length || _eof)
            return;

        PrepareForRefill();
        var buffer = _pool.Rent(_bufferSize);
        var length = await _reader.ReadAsync(buffer.AsMemory(0, _bufferSize), cancellationToken).ConfigureAwait(false);
        if (length == 0)
        {
            _pool.Return(buffer);
            _eof = true;
            _buffer = null;
            _length = 0;
            _index = 0;
            return;
        }

        _buffer = buffer;
        _length = length;
        _index = 0;
        if (_capturing)
            _captureStart = 0;
    }

    private void PrepareForRefill()
    {
        if (_buffer is null)
            return;

        if (_capturing)
        {
            var length = _index - _captureStart;
            if (length > 0)
            {
                _segments ??= [];
                _segments.Add(new BufferSegment(_buffer, _captureStart, length));
                _buffer = null;
                _captureStart = 0;
                return;
            }
        }

        if (_protectedCapture?.BackingArray == _buffer)
            _protectedBuffer = _buffer;
        else
            _pool.Return(_buffer);
        _buffer = null;
    }

    private void ReleaseSegments()
    {
        if (_segments is null)
            return;

        foreach (var segment in _segments)
            _pool.Return(segment.Array);
        _segments = null;
    }

    private void Advance(int count)
    {
        _index += count;
        Position += count;
    }

    private static void ValidateScanResult(FrameScanResult result)
    {
        if (result.Consumed <= 0)
            throw new InvalidOperationException("A record framer must consume available input.");
    }

    private void Return(ref char[]? array)
    {
        if (array is null)
            return;
        _pool.Return(array);
        array = null;
    }

    public void Dispose()
    {
        ReleaseSegments();
        Return(ref _ownedRecord);
        Return(ref _protectedBuffer);
        Return(ref _buffer);
    }

    private readonly record struct BufferSegment(char[] Array, int Start, int Length);
}

internal readonly record struct CapturedRecord(ReadOnlyMemory<char> Memory, char[]? BackingArray);
