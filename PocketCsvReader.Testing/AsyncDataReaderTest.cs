using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class AsyncDataReaderTest
{
    [Test]
    public async Task ReadAsync_IteratesRecordsAndKeepsTypedGettersSynchronous()
    {
        await using var reader = CreateReader("1,Ada\n2,Grace");

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.InstanceOf<IAsyncDataReader>());
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
        });
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ReadAsync_RecordCrossingBufferBoundaries_ReturnsCompleteFields()
    {
        var profile = WithBufferSize(Profile(), 4);
        await using var reader = new CsvReader(profile).ToDataReader(Text("abcdefgh,42\nx,7"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("abcdefgh"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(42));
        });
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("x"));
    }

    [Test]
    public async Task ReadAsync_AsyncOnlyNonSeekableStream_DoesNotUseSynchronousReads()
    {
        await using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("1,Ada\n"));
        await using var reader = new CsvReader(Profile()).ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ReadAsync_CancellationIsForwardedAndReaderCanBeDisposed()
    {
        await using var stream = new BlockingAsyncStream();
        var reader = new CsvReader(Profile()).ToDataReader(stream);
        using var cancellation = new CancellationTokenSource();
        var read = reader.ReadAsync(cancellation.Token);

        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidOperationException>());
        cancellation.Cancel();

        Assert.That(async () => await read,
            Throws.InstanceOf<OperationCanceledException>());
        await Assert.DoesNotThrowAsync(async () => await reader.DisposeAsync());
    }

    [Test]
    public async Task ReadAsync_MalformedRecord_ThrowsInvalidDataException()
    {
        await using var reader = CreateReader("\"unterminated");

        Assert.That(async () => await reader.ReadAsync(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task ReadAsync_GzipInput_ReadsDecompressedRecords()
    {
        await using var compressed = Compress("1,Ada\n2,Grace");
        var profile = new CsvProfile(
            new DialectDescriptorBuilder()
                .WithDelimiter(',')
                .WithLineTerminator("\n")
                .WithoutHeader()
                .Build(),
            resource: new ResourceDescriptorBuilder()
                .WithEncoding("utf-8")
                .WithCompression("gz")
                .Build());
        await using var reader = new CsvReader(profile).ToDataReader(compressed);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Grace"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private static CsvDataReader CreateReader(string content)
        => new CsvReader(Profile()).ToDataReader(Text(content));

    private static CsvProfile Profile()
        => new(new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\n")
            .WithoutHeader()
            .Build());

    private static CsvProfile WithBufferSize(CsvProfile profile, int bufferSize)
    {
        profile.ParserOptimizations = profile.ParserOptimizations with { BufferSize = bufferSize };
        return profile;
    }

    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));

    private static MemoryStream Compress(string content)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            gzip.Write(bytes);
        }
        output.Position = 0;
        return output;
    }

    private sealed class AsyncOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous read used.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingAsyncStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
