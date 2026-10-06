using System.Data;
using System.Data.Common;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class DbDataReaderAdapterTest
{
    [Test]
    public async Task ToDbDataReader_ExposesStandardContractForSyncAndAsyncReads()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        await using DbDataReader reader = new CsvReader(profile).ToDbDataReader(Text("1,Ada\n2,Grace"));

        Assert.That(reader, Is.InstanceOf<IDataReader>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task HasRows_BuffersFirstRecordWithoutSkippingIt()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        await using var reader = new CsvReader(profile).ToDbDataReader(Text("1,Ada"));

        Assert.That(reader.HasRows, Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(reader.HasRows, Is.True);
    }

    [Test]
    public void HasRows_EmptyInput_IsFalse()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        using var reader = new CsvReader(profile).ToDbDataReader(Text(string.Empty));

        Assert.That(reader.HasRows, Is.False);
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public async Task ReadAsync_AsyncOnlyNonSeekableStream_DoesNotUseSynchronousReads()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        await using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("1,Ada\n"));
        await using var reader = new CsvReader(profile).ToDbDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ReadAsync_RecordCrossingBufferBoundary_ReturnsCompleteFields()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        profile.ParserOptimizations = profile.ParserOptimizations with { BufferSize = 4 };
        await using var reader = new CsvReader(profile).ToDbDataReader(Text("abcdefgh,42\nx,7"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("abcdefgh"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(42));
    }

    [Test]
    public void Read_MalformedRecord_ThrowsInvalidDataException()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        using var reader = new CsvReader(profile).ToDbDataReader(Text("\"unterminated"));

        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task ReadAsync_GzipInput_ReadsDecompressedRecord()
    {
        await using var compressed = Compress("1,Ada");
        var profile = new CsvProfile(
            new DialectDescriptorBuilder().WithDelimiter(',').WithLineTerminator("\n").WithoutHeader().Build(),
            resource: new ResourceDescriptorBuilder().WithEncoding("utf-8").WithCompression("gz").Build());
        await using var reader = new CsvReader(profile).ToDbDataReader(compressed);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
    }

    [Test]
    public async Task ReadAsync_ForwardsCancellationAndCloseAsyncDisposesStream()
    {
        var stream = new BlockingAsyncStream();
        var profile = new CsvProfile(',', '"', "\n", false);
        var reader = new CsvReader(profile).ToDbDataReader(stream);
        using var cancellation = new CancellationTokenSource();
        var read = reader.ReadAsync(cancellation.Token);

        cancellation.Cancel();

        Assert.That(async () => await read, Throws.InstanceOf<OperationCanceledException>());
        await reader.CloseAsync();
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsClosed, Is.True);
            Assert.That(stream.Disposed, Is.True);
        });
    }

    [Test]
    public async Task BatchReader_AdvancesAcrossStreamBoundary()
    {
        var profile = new CsvProfile(',', '"', "\n", false);
        await using var reader = new CsvReader(profile).ToDbDataReader(
            new Stream[] { Text("1,Ada"), Text("2,Grace") });

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));

    private static MemoryStream Compress(string content)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(content));
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
    }

    private sealed class BlockingAsyncStream : Stream
    {
        public bool Disposed { get; private set; }
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
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
