using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class FusedProjectionTests
{
    private static NdjsonDataReader Open(string content, int bufferSize, string terminator = "\n", bool fallback = false, bool asyncOnly = false)
    {
        Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        if (asyncOnly) stream = new AsyncOnlyStream(stream);
        return new NdjsonReaderBuilder()
            .WithDialect(d => fallback ? d.WithLineTerminator(terminator).WithCommentChar('#') : d.WithLineTerminator(terminator))
            .WithProjection(p => p.Property("text").Property("id"))
            .WithPositionTracking().WithResource(r => r.WithEncoding("utf-8"))
            .WithParserOptimizations(new(BufferSize: bufferSize, ReadAhead: false))
            .Build().ToDataReader(stream);
    }

    [Test]
    public async Task SplitRecordsReuseMetadataAndRetainMaterializedValues(
        [Values(1, 2, 17, 65536)] int bufferSize, [Values("\n", "\r\n")] string terminator,
        [Values(false, true)] bool asynchronous)
    {
        string first = "  {\"unused\":{\"items\":[true,null,-1.2e+3]},\"id\":1,\"te\\u0078t\":\"😀\\n" + new string('x', 8000) + "\"} \t";
        string second = "{\"text\":\"next\",\"id\":2,\"unused\":\"skip\\u002B\"}";
        using var reader = Open(terminator + first + terminator + second, bufferSize, terminator, asyncOnly: asynchronous);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        var fields = Fields(reader);
        string retained = reader.GetString(0);
        Assert.That(retained, Is.EqualTo("😀\n" + new string('x', 8000)));
        Assert.That(reader.GetInt32(1), Is.EqualTo(1));
        Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(terminator.Length));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(Fields(reader), Is.SameAs(fields));
        Assert.That(reader.GetString(0), Is.EqualTo("next"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(2));
        Assert.That(typeof(BaseRawRecord<NdjsonProfile>).GetField("_record", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader), Is.Null,
            "Field access must not force the public compatibility wrapper.");
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
        reader.Dispose();
        Assert.That(retained, Is.EqualTo("😀\n" + new string('x', 8000)));
    }

    private static FieldSpan[] Fields(NdjsonDataReader reader) => (FieldSpan[])typeof(BaseRawRecord<NdjsonProfile>)
        .GetProperty("CurrentFieldSpans", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;

    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":01}")]
    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":\"\\q\"}")]
    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":\"\\uD800\"}")]
    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":[true,]}")]
    [TestCase("{\"text\":\"ok\",\"id\":1} {}")]
    [TestCase("{\"text\":\"ok\"}")]
    [TestCase("{\"text\":\"ok\",\"id\":1")]
    [TestCase("{\"text\":\"ok\",\"id\":1e \t\r\n")]
    [TestCase("{\"text\":\"ok\",\"id\":1 \t\r\n")]
    [TestCase("{\"text\":\"ok\",\"id\":\"truncated \t")]

    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":\"raw\nnewline\"}")]
    [TestCase("{\"text\":\"ok\",\"id\":1,\"unused\":\"raw\r\nnewline\"}")]
    public async Task InvalidRecordsKeepFallbackDiagnostics(string content)
    {
        foreach (var (terminator, size, asynchronous) in
            from terminator in new[] { "\n", "\r\n" }
            from size in new[] { 1, 65536 }
            from asynchronous in new[] { false, true }
            select (terminator, size, asynchronous))
        {
            using var fused = Open(content, size, terminator, asyncOnly: asynchronous);
            using var fallback = Open(content, size, terminator, fallback: true, asyncOnly: asynchronous);
            async Task Read(NdjsonDataReader reader)
            {
                if (asynchronous) await reader.ReadAsync(); else reader.Read();
            }
            var expected = await Assert.ThrowsAsync<InvalidDataException>(async () => await Read(fallback));
            var actual = await Assert.ThrowsAsync<InvalidDataException>(async () => await Read(fused));
            Assert.That(actual!.Message, Is.EqualTo(expected!.Message), $"buffer {size}, terminator {terminator.Length}, async {asynchronous}");
        }
    }

    [Test]
    public async Task CompositeSelectedValuesAndMixedReads()
    {
        const string row = "{\"text\":[null,{\"nested\":\"x\\n\"}],\"id\":3}";
        using var reader = Open(row + "\r\n" + row + "\r\n" + row, 3, "\r\n");
        Assert.That(reader.Read(), Is.True);
        string retained = reader.GetRawString(0);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo(retained));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task CancellationIsObservedBeforeAdvancement()
    {
        using var reader = Open("{\"text\":\"one\",\"id\":1}\n{\"text\":\"two\",\"id\":2}", 1, asyncOnly: true);
        Assert.That(await reader.ReadAsync(), Is.True);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(cancellation.Token));
    }

    [Test]
    public async Task RawAllocationBudget([Values(false, true)] bool asynchronous, [Values(false, true)] bool projected)
    {
        const int rows = 2000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "{\"text\":\"unused\\n\",\"id\":42,\"ignored\":\"" + new string('x', 1000) + "\"}\n", rows * 2)));
        var builder = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithResource(r => r.WithEncoding("utf-8"))
            .WithParserOptimizations(new(BufferSize: 65536, ReadAhead: false));
        if (projected) builder.WithProjection(p => p.Property("text").Property("id"));
        using var reader = builder.Build().ToDataReader(new MemoryStream(data));
        for (int i = 0; i < rows; i++)
            if (asynchronous) await reader.ReadAsync(); else reader.Read();
        var fields = Fields(reader);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < rows; i++)
            if (asynchronous) await reader.ReadAsync(); else reader.Read();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Crossing a 64K buffer creates bounded segment bookkeeping. 32 bytes/row
        // excludes an 80-byte wrapper, field arrays, decoded text and full-row copies.
        Assert.That(allocated, Is.LessThan(rows * 32L));
        Assert.That(Fields(reader), Is.SameAs(fields));
    }

    [Test]
    public async Task LookaheadRefillKeepsCurrentMemory([Values(false, true)] bool asynchronous,
        [Values("\n", "\r\n")] string terminator)
    {
        const string prefix = "{\"text\":\"";
        const string suffix = "\",\"id\":1}";
        string text = new('x', 64 - prefix.Length - suffix.Length - terminator.Length);
        string row = prefix + text + suffix + terminator;
        using var reader = Open(row + "{\"text\":\"next\",\"id\":2}", 64, terminator, asyncOnly: asynchronous);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(text));
        Assert.That(reader.GetInt32(1), Is.EqualTo(1));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("next"));
    }

    [Test]
    public async Task NonstandardDialectUsesCompatibleFallback([Values("||", "aba", "\r")] string terminator,
        [Values(false, true)] bool asynchronous)
    {
        var builder = new NdjsonReaderBuilder().WithProjection(p => p.Property("text").Property("id"))
            .WithDialect(d => d.WithLineTerminator(terminator).WithCommentChar('#'))
            .WithParserOptimizations(new(BufferSize: 1, ReadAhead: false));
        string embedded = terminator == "\r" ? "\\r" : terminator;
        string content = "# comment" + terminator + "{\"id\":1,\"text\":\"inside" + embedded + "#quoted\"} #tail" + terminator;
        using var reader = builder.Build().ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("inside" + terminator + "#quoted"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(1));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [Test]
    public async Task RetainedArraySurvivesAdvancementAndDisposal()
    {
        using var reader = Open("{\"text\":[\"one\\n\",\"two\"],\"id\":1}\n{\"text\":[\"next\"],\"id\":2}", 2);
        Assert.That(await reader.ReadAsync(), Is.True);
        var retained = reader.GetArray(0);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray(0), Is.EqualTo(new[] { "next" }));
        reader.Dispose();
        Assert.That(retained, Is.EqualTo(new[] { "one\n", "two" }));
    }

    [Test]
    public async Task CustomWhitespaceRemainsOnFallback([Values(false, true)] bool asynchronous)
    {
        var profile = new NdjsonProfile(new(LineTerminator: "\n", Whitespaces: [' ', '\t', '\r', '\n', '~']),
            parserOptimizations: new(BufferSize: 1), projectedProperties: ["text", "id"]);
        using var reader = new NdjsonReader(profile).ToDataReader(new MemoryStream(
            Encoding.UTF8.GetBytes("~\n~{\"text\":\"value\",\"id\":7}~\n")));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("value"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(7));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [Test]
    public async Task OrderedProjectionValidatesUnselectedNestedValues(
        [Values("\n", "\r\n")] string terminator, [Values(false, true)] bool asynchronous)
    {
        var builder = new NdjsonReaderBuilder().WithOrderedProjection(p => p.Property("text").Property("id"))
            .WithDialect(d => d.WithLineTerminator(terminator)).WithParserOptimizations(new(BufferSize: 2));
        using var reader = builder.Build().ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"text\":\"one\",\"skip\":{\"nested\":[null,1e2]},\"id\":7}" + terminator +
            "{\"id\":8,\"text\":\"reordered\"}")));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("one"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(7));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            if (asynchronous) await reader.ReadAsync(); else reader.Read();
        });
    }
    private sealed class AsyncOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous I/O");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous I/O");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
