using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class BufferReuseTest
{
    [TestCase(false, "")] [TestCase(true, "")]
    [TestCase(false, "Name")] [TestCase(true, "Name")]
    [TestCase(false, "Name\r\n")] [TestCase(true, "Name\r\n")]
    public async Task EmptyAndHeaderOnlyInput_HasNoDataRecords(bool async, string content)
    {
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(content), async);
        using var reader = new FixedWidthReaderBuilder().WithHeader().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 16)).WithField("Value", 0, 4).Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [Test]
    public async Task AlternatingSyncAndAsyncReads_UsesTheSameFramingState()
    {
        var values = Enumerable.Range(0, 4).Select(i => new string((char)('a' + i), 35)).ToArray();
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 16)).WithField("Value", 0, 35).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", values))));
        for (var i = 0; i < values.Length; i++)
        {
            Assert.That(i % 2 == 0 ? reader.Read() : await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo(values[i]));
        }
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase(false, 14)] [TestCase(true, 14)]
    [TestCase(false, 15)] [TestCase(true, 15)]
    [TestCase(false, 16)] [TestCase(true, 16)]
    [TestCase(false, 129)] [TestCase(true, 129)]
    public async Task SplitRecordsAndTerminators_PreserveValuesAndPositions(bool async, int width)
    {
        var values = new[] { "😀" + new string('é', width - 2), new string('x', width), new string('y', width) };
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(string.Join("\r\n", values)), async);
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 16)).WithPositionTracking()
            .WithField("Value", 0, width, FixedWidthPadding.None).Build().ToDataReader(stream);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.GetRawString(0), Is.EqualTo(values[i]));
            Assert.That(reader.GetString(0), Is.EqualTo(values[i]));
            Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(i + 1, i + 1, i * (width + 2), PositionOffsetUnit.Utf16CodeUnits)));
            await Task.Yield();
            Assert.That(reader.GetRawString(0), Is.EqualTo(values[i]), "Current record survives until the next read");
        }
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
        reader.Dispose();
        Assert.That(stream.Disposed, Is.True);
    }

    [TestCase(false)] [TestCase(true)]
    public async Task HeaderPaddingAndOverlappingTerminator_WorkAcrossRefills(bool async)
    {
        const string terminator = "ababac";
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes("Number  " + terminator + "00042   " + terminator + "00007   "), async);
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator(terminator).WithHeader()
            .WithParserOptimizations(new(BufferSize: 16))
            .WithField("Value", 0, 8, FixedWidthPadding.Both, ' ').Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("Number"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [TestCase(false, "ab", 2)] [TestCase(true, "ab", 2)]
    [TestCase(false, "abcababax", 9)] [TestCase(true, "abcababax", 9)]
    public async Task PartialAndOverlappingTerminatorAtEof_IsRecordData(bool async, string value, int width)
    {
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(value), async);
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("ababac")
            .WithParserOptimizations(new(BufferSize: 16))
            .WithField("Value", 0, width, FixedWidthPadding.None).Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo(value));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [TestCase(false, false)] [TestCase(true, false)]
    [TestCase(false, true)] [TestCase(true, true)]
    public async Task InvalidWidthsAcrossRefills_KeepRecordNumberAndActualWidth(bool async, bool terminated)
    {
        var content = new string('a', 35) + "\r\n" + new string('b', 34) + (terminated ? "\r\n" : "");
        using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(content), async);
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 16)).WithField("Value", 0, 35).Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => { if (async) await reader.ReadAsync(); else reader.Read(); });
        Assert.That(error!.Message, Is.EqualTo("Fixed-width record 2 is too short: expected 35 characters but found 34."));
    }

    [TestCase(false, false)] [TestCase(true, false)]
    [TestCase(false, true)] [TestCase(true, true)]
    public async Task LongTail_IsCountedWithoutUnboundedSpillStorage(bool async, bool allowed)
    {
        var profile = new FixedWidthProfile(new FixedWidthDescriptor([new("Value", 0, 3)], LineTerminator: "\r\n", AllowTrailingCharacters: allowed), parserOptimizations: new(BufferSize: 16));
        using var input = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("abc" + new string('x', 100_000) + "\r\n")));
        using var parser = new FixedWidthRecordParser(input, profile);
        if (!allowed)
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () => { if (async) await parser.ReadAsync(); else parser.IsEndOfFile(out _, out _); });
            Assert.That(error!.Message, Does.Contain("found 100003"));
            return;
        }
        var record = async ? (await parser.ReadAsync()).Record : Read(parser);
        Assert.That(record.Slice(0).ToString(), Is.EqualTo("abc"));
        Assert.That(MemoryMarshal.TryGetArray(record.Span, out var storage), Is.True);
        Assert.That(storage.Array!.Length, Is.LessThanOrEqualTo(16));
    }

    [TestCase(false, 3)] [TestCase(true, 3)]
    [TestCase(false, 128)] [TestCase(true, 128)]
    public async Task Parser_ReusesCharacterAndFieldArraysUntilDisposed(bool async, int width)
    {
        var profile = new FixedWidthProfile(new FixedWidthDescriptor([new("Value", 0, width)], LineTerminator: "\n"), parserOptimizations: new(BufferSize: 16));
        using var input = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(new string('a', width) + "\n" + new string('b', width) + "\n")));
        var parser = new FixedWidthRecordParser(input, profile);
        try
        {
            var first = async ? (await parser.ReadAsync()).Record : Read(parser);
            MemoryMarshal.TryGetArray(first.Span, out var firstStorage);
            var second = async ? (await parser.ReadAsync()).Record : Read(parser);
            MemoryMarshal.TryGetArray(second.Span, out var secondStorage);
            Assert.That(secondStorage.Array, Is.SameAs(firstStorage.Array));
            Assert.That(second.FieldSpans, Is.SameAs(first.FieldSpans));
            Assert.That(second.Slice(0).ToString(), Is.EqualTo(new string('b', width)));
        }
        finally { parser.Dispose(); }
        Assert.DoesNotThrow(parser.Dispose);
        Assert.That(() => Read(parser), Throws.TypeOf<ObjectDisposedException>());
    }

    [TestCase(false)] [TestCase(true)]
    public async Task EarlyDisposal_AfterSpilledRecord_ClosesNonSeekableStream(bool async)
    {
        var stream = new ChunkedStream(Encoding.UTF8.GetBytes(new string('x', 128) + "\n" + new string('y', 128)), async);
        var reader = new FixedWidthReaderBuilder().WithLineTerminator("\n").WithParserOptimizations(new(BufferSize: 16))
            .WithField("Value", 0, 128).Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        if (async) await reader.DisposeAsync(); else reader.Dispose();
        reader.Dispose();
        Assert.That(stream.Disposed, Is.True);
    }

    private static RecordMemory Read(FixedWidthRecordParser parser)
    {
        parser.IsEndOfFile(out var record, out _);
        return record.AsMemory();
    }

    private sealed class ChunkedStream(byte[] content, bool asyncOnly) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O is forbidden.");
            return _inner.Read(buffer, offset, Math.Min(count, 7));
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _inner.Dispose(); Disposed = true; }
            base.Dispose(disposing);
        }
    }
}

