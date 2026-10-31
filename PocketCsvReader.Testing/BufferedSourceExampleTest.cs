using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

// Compiled reference for new line-based formats. The example grammar has two fields separated by '|'.
public class BufferedSourceExampleTest
{
    [TestCase(4)]
    [TestCase(64)]
    [TestCase(4096)]
    public void Sync_ReadsRecordsAcrossBuffers(int bufferSize)
    {
        var content = "left|right\r\n" + new string('x', 10000) + "|é\nlast|value";
        var expected = new[] { ("left", "right"), (new string('x', 10000), "é"), ("last", "value") };
        using var text = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        using var source = new ExampleSource(text, bufferSize);
        foreach (var (left, right) in expected)
        {
            Assert.That(source.IsEndOfFile(out var record, out var state), Is.False);
            Assert.That(state, Is.EqualTo(RecordState.Record));
            Assert.That(record.Slice(0).ToString(), Is.EqualTo(left));
            Assert.That(record.Slice(1).ToString(), Is.EqualTo(right));
        }
        Assert.That(source.IsEndOfFile(out _, out _), Is.True);
    }

    [TestCase(4)]
    [TestCase(64)]
    [TestCase(4096)]
    public async Task Async_ReadsWithoutSynchronousIOOrSeeking(int bufferSize)
    {
        using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("first|value\r\n" + new string('x', 10000) + "|é"));
        using var text = new StreamReader(stream);
        using var source = new ExampleSource(text, bufferSize);

        var first = await source.ReadAsync();
        Assert.That(first.Record.Slice(0).ToString(), Is.EqualTo("first"));
        var second = await source.ReadAsync();
        Assert.That(second.Record.Slice(0).Length, Is.EqualTo(10000));
        Assert.That(second.Record.Slice(1).ToString(), Is.EqualTo("é"));
        Assert.That((await source.ReadAsync()).IsEndOfFile, Is.True);
    }

    [Test]
    public void RecordsWithinOneBuffer_ReuseCharacterAndFieldStorage()
    {
        using var stream = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("first|value\nnext|other\n")));
        using var source = new ExampleSource(stream, 4096);
        source.IsEndOfFile(out var first, out _);
        var fields = first.FieldSpans;
        Assert.That(MemoryMarshal.TryGetArray(first.Memory, out var firstMemory), Is.True);
        var retainedValue = first.Slice(0).ToString();

        source.IsEndOfFile(out var second, out _);
        Assert.That(MemoryMarshal.TryGetArray(second.Memory, out var secondMemory), Is.True);
        Assert.That(secondMemory.Array, Is.SameAs(firstMemory.Array));
        Assert.That(second.FieldSpans, Is.SameAs(fields));
        Assert.That(second.Slice(0).ToString(), Is.EqualTo("next"));
        Assert.That(retainedValue, Is.EqualTo("first"));
        // first and fields are borrowed views; only retainedValue is promised to survive the next read.
    }

    [Test]
    public void RawParsing_DoesNotAllocateWholeRecordCopiesOrFieldArraysPerRow()
    {
        const int rows = 1000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(new string('x', 128) + "|value\n", rows)));
        ReadAll(data); // Warm the JIT and shared character pool outside measurement.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = ReadAll(data);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(count, Is.EqualTo(rows));
        // Includes setup and cross-buffer bookkeeping. A per-row line string or character/field array exceeds this budget.
        Assert.That(allocated, Is.LessThan(64L * rows), $"Allocated {allocated} bytes for {rows} raw records.");

        static int ReadAll(byte[] data)
        {
            using var text = new StreamReader(new MemoryStream(data));
            using var source = new ExampleSource(text, 4096);
            var count = 0;
            while (!source.IsEndOfFile(out _, out _))
                count++;
            return count;
        }
    }

    [TestCase("")]
    [TestCase("\n")]
    [TestCase("missing delimiter\n")]
    public void EmptyOrMalformedInput_HasDefinedBehavior(string content)
    {
        using var text = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        using var source = new ExampleSource(text, 4);
        if (content.Length == 0)
            Assert.That(source.IsEndOfFile(out _, out _), Is.True);
        else
            Assert.Throws<InvalidDataException>(() => source.IsEndOfFile(out _, out _));
    }

    // The input StreamReader remains owned by the caller, as it does with BaseDataReader.
    private sealed class ExampleSource(StreamReader reader, int bufferSize) : IRecordSource<ExampleProfile>
    {
        private readonly BufferedRecordReader _reader = new(reader, bufferSize);
        private readonly FieldSpan[] _fields = new FieldSpan[2];
        public ExampleProfile Profile { get; } = new();

        public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
        {
            _reader.BeginOperation(); // The previous record is no longer borrowed by the consumer.
            var capture = _reader.ReadLineFrame();
            if (capture is null)
            {
                record = new RecordSpan([], []);
                recordState = RecordState.Eof;
                return true;
            }
            record = Parse(capture.Value.Memory);
            recordState = RecordState.Record;
            return false;
        }

        public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reader.BeginOperation();
            var capture = await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
            return capture is null
                ? new(true, RecordMemory.Empty, RecordState.Eof)
                : new(false, Parse(capture.Value.Memory).AsMemory(), RecordState.Record);
        }

        private RecordSpan Parse(ReadOnlyMemory<char> memory)
        {
            var separator = memory.Span.IndexOf('|');
            if (separator < 0)
                throw new InvalidDataException("The example record requires a '|' separator.");
            _fields[0] = new FieldSpan(0, separator);
            _fields[1] = new FieldSpan(separator + 1, memory.Length - separator - 1);
            // The character and metadata views remain valid until the next read or disposal.
            return RecordSpan.FromMemory(memory, _fields);
        }

        public void Dispose() => _reader.Dispose();
    }

    private sealed class ExampleProfile : IProfile
    {
        public SchemaDescriptor? Schema => null;
        public ResourceDescriptor? Resource => null;
        public RuntimeParsersDescriptor? Parsers => null;
    }

    private sealed class AsyncOnlyStream(byte[] data) : MemoryStream
    {
        private int _offset;
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous IO is forbidden.");
        public override int Read(Span<byte> buffer) => throw new NotSupportedException("Synchronous IO is forbidden.");
        public override int ReadByte() => throw new NotSupportedException("Synchronous IO is forbidden.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, data.Length - _offset);
            data.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }
    }
}
