using System.Text;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

internal sealed class AsyncOnlyInput(string input, int chunkSize, CancellationTokenSource? cancelOnRead = null) : Stream
{
    private readonly MemoryStream _input = new(Encoding.UTF8.GetBytes(input));

    public bool Disposed { get; private set; }
    public int BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous read.");
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancelOnRead?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        var read = _input.Read(buffer.Span[..Math.Min(buffer.Length, chunkSize)]);
        BytesRead += read;
        return ValueTask.FromResult(read);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Disposed = true;
            _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
