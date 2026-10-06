using System.Buffers;

namespace PocketCsvReader.Json;

internal sealed class BufferedJsonCursor : IDisposable
{
    private readonly IBufferReader _reader;
    private readonly ArrayPool<char> _pool;
    private ReadOnlyMemory<char> _buffer;
    private int _index;
    private bool _initialized;
    private bool _capturing;
    private int _captureStart;
    private char[]? _captureBuffer;
    private int _captureLength;
    private CapturedJson? _protectedCapture;
    private char[]? _ownedRecord;

    public long Position { get; private set; }

    public BufferedJsonCursor(StreamReader reader, int bufferSize, ArrayPool<char>? pool = null)
    {
        _pool = pool ?? ArrayPool<char>.Shared;
        _reader = new SingleBuffer(reader, bufferSize, _pool);
    }

    public void BeginOperation()
    {
        _protectedCapture = null;
        if (_ownedRecord is null)
            return;

        _pool.Return(_ownedRecord);
        _ownedRecord = null;
    }

    public int Peek()
    {
        EnsureAvailable();
        return _index < _buffer.Length ? _buffer.Span[_index] : -1;
    }

    public async ValueTask<int> PeekAsync(CancellationToken cancellationToken)
    {
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        return _index < _buffer.Length ? _buffer.Span[_index] : -1;
    }

    public int Read()
    {
        var current = Peek();
        if (current >= 0)
        {
            _index++;
            Position++;
        }
        return current;
    }

    public async ValueTask<int> ReadAsync(CancellationToken cancellationToken)
    {
        var current = await PeekAsync(cancellationToken).ConfigureAwait(false);
        if (current >= 0)
        {
            _index++;
            Position++;
        }
        return current;
    }

    public void BeginCapture()
    {
        if (_capturing)
            throw new InvalidOperationException("A JSON value is already being captured.");

        _capturing = true;
        _captureStart = _index;
        _captureLength = 0;
        _captureBuffer = null;
    }

    public CapturedJson EndCapture(int trimEnd = 0)
    {
        if (!_capturing)
            throw new InvalidOperationException("No JSON value is being captured.");

        var segmentLength = _index - _captureStart - trimEnd;
        if (segmentLength < 0)
            throw new ArgumentOutOfRangeException(nameof(trimEnd));

        ReadOnlyMemory<char> memory;
        var isOwned = _captureBuffer is not null;
        if (_captureBuffer is null)
        {
            memory = _buffer.Slice(_captureStart, segmentLength);
        }
        else
        {
            AppendCapture(_buffer.Span.Slice(_captureStart, segmentLength));
            _ownedRecord = _captureBuffer;
            memory = _captureBuffer.AsMemory(0, _captureLength);
        }

        _capturing = false;
        _captureBuffer = null;
        _captureLength = 0;
        return new CapturedJson(memory, isOwned);
    }

    public void Protect(CapturedJson capture)
        => _protectedCapture = capture;

    private void EnsureAvailable()
    {
        if (_index < _buffer.Length || (_initialized && _reader.IsEof))
            return;

        PrepareForRefill();
        _buffer = _reader.Read();
        _index = 0;
        _initialized = true;
    }

    private async ValueTask EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        if (_index < _buffer.Length || (_initialized && _reader.IsEof))
            return;

        PrepareForRefill();
        _buffer = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        _index = 0;
        _initialized = true;
    }

    private void PrepareForRefill()
    {
        if (_capturing)
        {
            AppendCapture(_buffer.Span.Slice(_captureStart, _index - _captureStart));
            _captureStart = 0;
        }

        if (_protectedCapture is not null && !_protectedCapture.IsOwned)
            PreserveProtectedCapture();
    }

    private void PreserveProtectedCapture()
    {
        var memory = _protectedCapture!.Memory;
        var rented = _pool.Rent(memory.Length);
        memory.Span.CopyTo(rented);
        _ownedRecord = rented;
        _protectedCapture.Memory = rented.AsMemory(0, memory.Length);
        _protectedCapture.IsOwned = true;
    }

    private void AppendCapture(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
            return;

        EnsureCaptureCapacity(_captureLength + segment.Length);
        segment.CopyTo(_captureBuffer.AsSpan(_captureLength));
        _captureLength += segment.Length;
    }

    private void EnsureCaptureCapacity(int required)
    {
        if (_captureBuffer is not null && _captureBuffer.Length >= required)
            return;

        var replacement = _pool.Rent(Math.Max(required, Math.Max(_buffer.Length, 256)));
        if (_captureBuffer is not null)
        {
            _captureBuffer.AsSpan(0, _captureLength).CopyTo(replacement);
            _pool.Return(_captureBuffer);
        }
        _captureBuffer = replacement;
    }

    public void Dispose()
    {
        if (_captureBuffer is not null && _captureBuffer != _ownedRecord)
            _pool.Return(_captureBuffer);
        if (_ownedRecord is not null)
            _pool.Return(_ownedRecord);
        _reader.Dispose();
    }
}

internal sealed class CapturedJson(ReadOnlyMemory<char> memory, bool isOwned)
{
    public ReadOnlyMemory<char> Memory { get; set; } = memory;
    public bool IsOwned { get; set; } = isOwned;
}
