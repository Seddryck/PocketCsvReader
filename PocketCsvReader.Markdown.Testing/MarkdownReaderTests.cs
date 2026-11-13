using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Markdown.Configuration;

namespace PocketCsvReader.Markdown.Testing;

public class MarkdownReaderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadsPipeRowsWithGfmCellsFromNonSeekableInput(bool asynchronous)
    {
        const string text = "Intro | prose\r\n  | Id | Head\\|er | Notes |\n\t| :-- | :-: | --: |\r"
            + "| 42 | `a\\|b` | [link](https://example.com) |\r\n"
            + "ignore | this\n | 7 |  ||\n | 8\n | 9 | café 🦊";
        using var stream = new ChunkedStream(text, chunkSize: 3, rejectSync: asynchronous);
        using var reader = new MarkdownReaderBuilder()
            .WithParserOptimizations(new(BufferSize: 5))
            .WithPositionTracking()
            .Build().ToDataReader(stream);

        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "Id", "Head|er", "Notes" }));
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetRawString(1), Is.EqualTo("`a\\|b`"));
        Assert.That(reader.GetString(1), Is.EqualTo("`a|b`"));
        Assert.That(reader.GetString(2), Is.EqualTo("[link](https://example.com)"));
        Assert.That(reader.CurrentPosition, Is.EqualTo(
            new RecordPosition(1, 4, text.IndexOf("| 42", StringComparison.Ordinal), PositionOffsetUnit.Utf16CodeUnits)));

        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
        Assert.That(reader.GetString(1), Is.Empty);
        Assert.That(reader.GetString(2), Is.Empty);
        Assert.That(reader.CurrentPosition?.PhysicalLine, Is.EqualTo(6));

        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetString(1), Is.Empty);
        Assert.That(reader.GetString(2), Is.Empty);
        Assert.That(reader.IsDBNull(2), Is.False);
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("café 🦊"));
        Assert.That(await Read(reader, asynchronous), Is.False);
    }

    [TestCase(0, "First", "one")]
    [TestCase(1, "Second", "two")]
    [TestCase(2, "Empty", null)]
    [TestCase(3, "Fourth", "four")]
    [TestCase(4, null, null)]
    public void SkipsOnlyValidTablesAndStopsAtTheNextTable(int skip, string? header, string? value)
    {
        const string text = "| Mismatch | Other |\n| -- |\n"
            + "| Broken |\n| : |\n"
            + "| Interrupted |\nignored\n| -- |\n\n"
            + "| First |\n| -- |\n| one |\n"
            + "| Second |\n| ---\n| two\n\n"
            + "| Empty |\n| --- |\n"
            + "| Fourth |\n| --- |\n| four |\n";
        using var reader = new MarkdownReaderBuilder().WithSkipTables(skip).Build().ToDataReader(StreamOf(text));
        Assert.That(reader.Read(), Is.EqualTo(value is not null));
        if (header is not null) Assert.That(reader.GetName(0), Is.EqualTo(header));
        else Assert.That(reader.FieldCount, Is.Zero);
        if (value is not null) Assert.That(reader.GetString(0), Is.EqualTo(value));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MarkerMatchesAnExactTrimmedLine(bool asynchronous)
    {
        const string text = "| Earlier |\n| -- |\n| old |\n\n"
            + "Target suffix\ntarget\n  Target \t\n\n \t\n  | Name |\n | -- |\n | Ada |\n\n";
        using var reader = new MarkdownReaderBuilder().WithTableAfter(" Target ").Build()
            .ToDataReader(new ChunkedStream(text, 2, asynchronous));
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("Name"));
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(await Read(reader, asynchronous), Is.False);
    }

    [TestCase("Different marker\n")]
    [TestCase("Target\nIntervening prose\n")]
    [TestCase("Target\n| Broken |\n| -- | -- |\n")]
    [TestCase("Target\n| Interrupted |\n\n")]
    [TestCase("Target\nprose\nTarget\n")]
    public void AbsentMarkerOrImmediateTableYieldsNoRows(string preamble)
    {
        const string table = "| Name |\n| -- |\n| Ada |\n";
        using var reader = new MarkdownReaderBuilder().WithTableAfter("Target").Build()
            .ToDataReader(StreamOf(preamble + table));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.FieldCount, Is.Zero);
    }

    [TestCase("")]
    [TestCase(" \r\n")]
    [TestCase("| Header |")]
    [TestCase("Name | Value\n--- | ---\n1 | 2\n")]
    [TestCase("| Header |\n| :- : |")]
    public void InputWithoutAValidTableYieldsNoRows(string text)
    {
        using var reader = new MarkdownReaderBuilder().Build().ToDataReader(StreamOf(text));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.FieldCount, Is.Zero);
    }

    [Test]
    public void InvalidSelectionSettingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarkdownReaderBuilder().WithSkipTables(-1));
        Assert.Throws<ArgumentException>(() => new MarkdownReaderBuilder().WithTableAfter(" "));
        Assert.Throws<ArgumentException>(() => new MarkdownReaderBuilder().WithTableAfter("A\nB").Build());
        Assert.Throws<ArgumentException>(() => new MarkdownReaderBuilder().WithTableAfter("A").WithSkipTables(1).Build());
    }

    [Test]
    public void OrdinaryRowSkipAppliesAfterTableSelection()
    {
        const string text = "| Key |\n| -- |\n| old |\n\n| Key |\n| -- |\n| first |\n| second |\n| third |";
        var markdown = new MarkdownReaderBuilder().WithSkipTables(1).WithSkip(1).WithTake(1).Build();
        using var input = StreamOf(text);
        Assert.That(markdown.ToArrayString(input).Single(), Is.EqualTo(new[] { "second" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SurplusCellsThrowByDefault(bool asynchronous)
    {
        const string text = "| Name |\n| -- |\n| Ada | Extra |\n| Next |";
        using var reader = new MarkdownReaderBuilder().Build().ToDataReader(StreamOf(text));
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await Read(reader, asynchronous));
        Assert.That(error!.Message, Does.Contain("line 3"));
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Next"));
    }

    [TestCase(BadDataAction.SkipRecord, "Next")]
    [TestCase(BadDataAction.ReturnPartialRecord, "Ada")]
    [TestCase(BadDataAction.Stop, null)]
    public async Task SurplusCellsUseConfiguredRecovery(BadDataAction action, string? firstValue)
    {
        const string text = "Intro\r\n| Name |\r\n| -- |\r\n| Ada | Extra |\r\n| Next |";
        BadDataContext? observed = null;
        using var reader = new MarkdownReaderBuilder().WithPositionTracking()
            .WithBadDataPolicy(new(context => { observed = context; return action; }))
            .Build().ToDataReader(StreamOf(text));
        Assert.That(await reader.ReadAsync(), Is.EqualTo(firstValue is not null));
        if (firstValue is not null) Assert.That(reader.GetString(0), Is.EqualTo(firstValue));
        Assert.That(observed?.RecordNumber, Is.EqualTo(1));
        Assert.That(observed?.PhysicalLine, Is.EqualTo(4));
        Assert.That(observed?.FieldIndex, Is.EqualTo(1));
        Assert.That(observed?.Offset, Is.EqualTo(text.IndexOf("Extra", StringComparison.Ordinal)));
        Assert.That(observed?.OffendingInput, Is.EqualTo("Extra"));
        Assert.That(observed?.RecordPosition?.Offset, Is.EqualTo(text.IndexOf("| Ada", StringComparison.Ordinal)));
    }

    [Test]
    public void SurplusCellsRespectMaximumErrorCount()
    {
        const string text = "| Name |\n| -- |\n| Ada | Extra |\n| Bob | Extra |";
        using var reader = new MarkdownReaderBuilder()
            .WithBadDataPolicy(new(_ => BadDataAction.SkipRecord, maximumErrors: 1))
            .Build().ToDataReader(StreamOf(text));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("maximum bad-data count"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReturnsRowsBeforeReadingTheRestOfTheInput(bool asynchronous)
    {
        string text = "| Name |\n| -- |\n| First |\n| Second |\n\n" + new string('x', 20_000);
        using var input = new ChunkedStream(text, 29, asynchronous);
        using var reader = new MarkdownReaderBuilder().Build().ToDataReader(input);
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("First"));
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(await Read(reader, asynchronous), Is.False);
        Assert.That(input.BytesRead, Is.LessThan(1024));
    }

    [Test]
    public async Task PreCancelledReadCanBeRetried()
    {
        using var reader = new MarkdownReaderBuilder().Build().ToDataReader(StreamOf("| Name |\n| -- |\n| Ada |\n| Bob |"));
        Assert.That(await reader.ReadAsync(), Is.True);
        await Assert.CatchAsync<OperationCanceledException>(async () => await reader.ReadAsync(new CancellationToken(true)));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Bob"));
    }

    [Test]
    public async Task CancellationDuringARefillEndsTheForwardOnlyRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ChunkedStream("| Header |\n| -- |\n| value |", 4, rejectSync: true);
        stream.BeforeAsyncRead = () =>
        {
            if (stream.BytesRead >= 4) cancellation.Cancel();
        };
        using var reader = new MarkdownReaderBuilder().WithParserOptimizations(new(BufferSize: 4))
            .Build().ToDataReader(stream);

        await Assert.CatchAsync<OperationCanceledException>(async () => await reader.ReadAsync(cancellation.Token));
        Assert.That(stream.BytesRead, Is.EqualTo(4));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task BorrowedFieldsAndBufferedLinesAreReused()
    {
        const string text = "| Name |\n| -- |\n| Fir\\|st |\n| Second |\n";
        using var reader = new MarkdownReaderBuilder().Build().ToDataReader(StreamOf(text));
        var rawType = typeof(BaseRawRecord<MarkdownProfile>);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var metadata = rawType.GetProperty("CurrentFieldSpans", flags)!;
        var memory = rawType.GetProperty("CurrentRecordMemory", flags)!;
        var wrapper = rawType.GetField("_record", flags)!;
        Assert.That(reader.Read(), Is.True);
        var fields = metadata.GetValue(reader);
        var firstMemory = (ReadOnlyMemory<char>)memory.GetValue(reader)!;
        var retained = reader.GetString(0);
        Assert.That(wrapper.GetValue(reader), Is.Null);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(metadata.GetValue(reader), Is.SameAs(fields));
        Assert.That(MemoryMarshal.TryGetArray(firstMemory, out var firstBuffer), Is.True);
        Assert.That(MemoryMarshal.TryGetArray((ReadOnlyMemory<char>)memory.GetValue(reader)!, out var secondBuffer), Is.True);
        Assert.That(secondBuffer.Array, Is.SameAs(firstBuffer.Array));
        Assert.That(reader.GetString(0), Is.EqualTo("Second"));
        Assert.That(retained, Is.EqualTo("Fir|st"));
        Assert.That(wrapper.GetValue(reader), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RawReadsHaveBoundedAllocations(bool asynchronous)
    {
        const int rows = 4000;
        const string row = "| Some long text to expose full-row copies | 123 | Escaped \\| text |\n";
        byte[] data = Encoding.UTF8.GetBytes("| Name | Number | Notes |\n| -- | -- | -- |\n" + string.Concat(Enumerable.Repeat(row, rows)));
        var markdown = new MarkdownReaderBuilder().Build();
        int ReadAll()
        {
            using var reader = markdown.ToDataReader(new MemoryStream(data));
            int count = 0;
            while (asynchronous ? reader.ReadAsync().GetAwaiter().GetResult() : reader.Read())
                count++;
            return count;
        }

        for (int i = 0; i < 3; i++) ReadAll();
        long start = GC.GetAllocatedBytesForCurrentThread();
        int count = ReadAll();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.That(count, Is.EqualTo(rows));
        // Includes reader setup and occasional cross-buffer rows. Rejects a wrapper, metadata array, or string per row.
        Assert.That(allocated, Is.LessThan(32L * rows), $"Allocated {allocated} bytes.");
    }

    private static ValueTask<bool> Read(MarkdownDataReader reader, bool asynchronous)
        => asynchronous ? new(reader.ReadAsync()) : ValueTask.FromResult(reader.Read());

    private static MemoryStream StreamOf(string text) => new(Encoding.UTF8.GetBytes(text));

    private sealed class ChunkedStream(string text, int chunkSize, bool rejectSync) : Stream
    {
        private readonly byte[] _data = Encoding.UTF8.GetBytes(text);
        public int BytesRead { get; private set; }
        public Action? BeforeAsyncRead { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => rejectSync ? throw new InvalidOperationException("Synchronous input is disabled.") : CopyTo(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BeforeAsyncRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CopyTo(buffer.Span));
        }

        private int CopyTo(Span<byte> destination)
        {
            int length = Math.Min(Math.Min(chunkSize, destination.Length), _data.Length - BytesRead);
            _data.AsSpan(BytesRead, length).CopyTo(destination);
            BytesRead += length;
            return length;
        }
    }
}
