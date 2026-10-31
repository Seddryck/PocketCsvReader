using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class BufferedKeyValueTests
{
    private static KeyValueReader Build(bool logfmt, int bufferSize) => logfmt
        ? new LogfmtReaderBuilder().WithParserOptimizations(new(BufferSize: bufferSize)).Build()
        : new LtsvReaderBuilder().WithParserOptimizations(new(BufferSize: bufferSize)).Build();

    [Test]
    public async Task BoundariesPreserveValuesAndSchema(
        [Values(false, true)] bool logfmt, [Values(false, true)] bool async,
        [Values(1, 2, 7, 32, 4096)] int bufferSize)
    {
        var expected = new string('x', 70_000) + "東京 café 😀";
        string first = logfmt ? $"id=1 message=\"{expected}\\n\\\"ok\\\"\" bare empty="
            : $"id:1\tmessage:{expected}:suffix\tbare:\tempty:";
        string last = logfmt ? "message=done id=2 id=3" : "message:done\tid:2\tid:3";
        foreach (string ending in new[] { "\n", "\r\n", "\r" })
        foreach (bool finalNewline in new[] { false, true })
        {
            using var stream = new ChunkedStream(first + ending + ending + last + (finalNewline ? ending : ""), async);
            using var reader = Build(logfmt, bufferSize).ToDataReader(stream);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.GetString(1), Is.EqualTo(expected + (logfmt ? "\n\"ok\"" : ":suffix")));
            Assert.That(reader.GetString(2), Is.Empty);
            Assert.That(reader.GetString(3), Is.Empty);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.FieldCount, Is.Zero);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(0), Is.EqualTo("message"));
            Assert.That(reader.GetString(0), Is.EqualTo("done"));
            Assert.That(reader.GetOrdinal("id"), Is.EqualTo(1));
            Assert.That(reader.GetInt32(2), Is.EqualTo(3));
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        }
    }

    [Test]
    public async Task EverySplitAroundDelimitersAndEscapes(
        [Values(false, true)] bool logfmt, [Values(false, true)] bool async)
    {
        string line = logfmt ? "id=1 message=\"東京\\n\\u263a\\\"\\\\\" bare empty="
            : "id:1\tmessage:東京:☺\tbare:\tempty:";
        for (int size = 1; size <= line.Length + 2; size++)
        {
            using var reader = Build(logfmt, size).ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(line + "\r\n" + line)));
            for (int row = 0; row < 2; row++)
            {
                Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
                Assert.That(reader.GetString(1), Is.EqualTo(logfmt ? "東京\n☺\"\\" : "東京:☺"));
                Assert.That(reader.FieldCount, Is.EqualTo(4));
            }
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        }
    }

    [TestCase(false, "valid:value\t")]
    [TestCase(false, "missing")]
    [TestCase(false, "bad label:value")]
    [TestCase(false, "a:bad\0value")]
    [TestCase(true, "message=\"bad\\q\"")]
    [TestCase(true, "message=\"bad\\")]
    [TestCase(true, "message=\"unterminated")]
    [TestCase(true, "message=\"done\"suffix")]
    public async Task MalformedRecordsKeepUsefulDiagnostics(bool logfmt, string line)
    {
        foreach (bool async in new[] { false, true })
        {
            using var reader = Build(logfmt, 2).ToDataReader(new ChunkedStream(line, async));
            var exception = async
                ? await Assert.ThrowsAsync<InvalidDataException>(async () => { await reader.ReadAsync(); })
                : Assert.Throws<InvalidDataException>(() => reader.Read());
            Assert.That(exception!.Message, Does.Contain("position"));
        }
        await Task.CompletedTask;
    }

    [Test]
    public void CurrentRecordBorrowsBufferAndReusesMetadata([Values(false, true)] bool logfmt)
    {
        string first = logfmt ? "a=one b=two" : "a:one\tb:two";
        string second = logfmt ? "b=three a=four" : "b:three\ta:four";
        using var text = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(first + "\n" + second)));
        using var source = new KeyValueRecordSource(text, new KeyValueProfile(logfmt ? KeyValueFormat.Logfmt : KeyValueFormat.Ltsv, parserOptimizations: new(BufferSize: 4096)));
        source.IsEndOfFile(out var record, out _);
        var current = record.AsMemory();
        Assert.That(MemoryMarshal.TryGetArray(current.Span, out var backing), Is.True);
        // A contained record points inside the large input buffer rather than a record-sized copy.
        Assert.That(backing.Array!.Length, Is.GreaterThan(first.Length));
        Assert.That(current.Span.ToString(), Is.EqualTo(first));
        Assert.That(current.Slice(1).ToString(), Is.EqualTo("two"));
        var fields = current.FieldSpans;
        source.IsEndOfFile(out var next, out _);
        Assert.That(ReferenceEquals(fields, next.FieldSpans), Is.True);
        Assert.That(next.Slice(0).ToString(), Is.EqualTo("three"));
        // Disposing the source returns its buffers without taking ownership of StreamReader.
        source.Dispose();
        Assert.That(text.BaseStream.CanRead, Is.True);
    }

    [Test]
    public void RetainedMaterializedOutputsSurviveBufferAndMetadataReuse([Values(false, true)] bool logfmt)
    {
        string content = logfmt ? "a=\"one\\n\" b=first\nb=second a=two\na=last"
            : "a:one\tb:first\nb:second\ta:two\na:last";
        var format = Build(logfmt, 3);
        var arrays = format.ToArrayString(new MemoryStream(Encoding.UTF8.GetBytes(content))).ToArray();
        var objects = format.ToObjectArrays(new MemoryStream(Encoding.UTF8.GetBytes(content))).ToArray();
        var dictionaries = format.ToDictionaries(new MemoryStream(Encoding.UTF8.GetBytes(content))).ToArray();
        Assert.That(arrays[0], Is.EqualTo(new[] { logfmt ? "one\n" : "one", "first" }));
        Assert.That(objects[0], Is.EqualTo(arrays[0]));
        Assert.That(dictionaries[0]["a"], Is.EqualTo(arrays[0][0]));
        Assert.That(dictionaries[1]["a"], Is.EqualTo("two"));
        Assert.That(arrays[2], Is.EqualTo(new[] { "last" }));
    }

    [Test]
    public async Task AsyncReaderOwnsStreamAndHonorsCancellation()
    {
        var stream = new ChunkedStream("a=one\na=two", asyncOnly: true);
        await using (var reader = Build(true, 2).ToDataReader(stream))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => { await reader.ReadAsync(cancellation.Token); });
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("one"));
        }
        Assert.That(stream.Disposed, Is.True);
    }

    // Non-seekable short reads also split UTF-8 scalars. Async mode fails on any sync I/O.
    private sealed class ChunkedStream(string text, bool asyncOnly) : Stream
    {
        private readonly MemoryStream _inner = new(Encoding.UTF8.GetBytes(text));
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O is forbidden.");
            return _inner.Read(buffer, offset, Math.Min(count, 127));
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_inner.Read(buffer.Span[..Math.Min(buffer.Length, 127)]));
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
