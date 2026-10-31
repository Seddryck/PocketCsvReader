using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class ReaderHandoffTest
{
    [TestCase(32, false)] [TestCase(32, true)]
    [TestCase(8192, false)] [TestCase(8192, true)]
    public async Task WarmedRawReads_AllocateWrappersWithoutRecordCharacterCopies(int width, bool async)
    {
        const int warmup = 4;
        const int records = 128;
        var content = string.Join("\r\n", Enumerable.Repeat(new string('x', width), warmup + records));
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 1024))
            .WithField("Value", 0, width, FixedWidthPadding.None).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        for (var i = 0; i < warmup; i++)
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);

        // MemoryStream completes async I/O inline, so this thread-local measurement
        // includes every read. Input creation, initialization, pool warmup, field
        // materialization, assertions, and disposal are outside the measured region.
        var thread = Environment.CurrentManagedThreadId;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        while (async ? await reader.ReadAsync() : reader.Read()) count++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread));
        Assert.That(count, Is.EqualTo(records));
        // Current RecordMemory wrappers cost 80 bytes/row on x64. Allow 96 bytes
        // plus 1 KiB slack, below even a single extra 32-character array per row.
        // The same budget for 8,192 characters rejects size-dependent record copies.
        Assert.That(allocated, Is.LessThanOrEqualTo((records * 96L) + 1024));
    }

    [TestCase(3)]
    [TestCase(128)]
    public void SynchronousReader_BorrowsParserCharactersAndFieldMetadata(int width)
    {
        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\r\n")
            .WithParserOptimizations(new(BufferSize: 16))
            .WithField("Value", 0, width, FixedWidthPadding.None).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(new string('a', width) + "\r\n" + new string('b', width) + "\r\n")));
        reader.Initialize();
        // Inspect ownership without adding a public testing API to the sealed reader.
        var source = (IRecordSource<FixedWidthProfile>)typeof(BaseDataReader<FixedWidthProfile>)
            .GetProperty("RecordSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        source.IsEndOfFile(out var parserRecord, out _);
        var parserMemory = parserRecord.AsMemory();
        MemoryMarshal.TryGetArray(parserMemory.Span, out var parserStorage);

        Assert.That(reader.Read(), Is.True);
        var readerRecord = (RecordMemory)typeof(BaseRawRecord<FixedWidthProfile>)
            .GetProperty("Record", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        MemoryMarshal.TryGetArray(readerRecord.Span, out var readerStorage);
        Assert.That(readerStorage.Array, Is.SameAs(parserStorage.Array), "The synchronous handoff must retain parser-owned characters");
        Assert.That(readerRecord.FieldSpans, Is.SameAs(parserMemory.FieldSpans));
        Assert.That(reader.GetRawString(0), Is.EqualTo(new string('b', width)));
    }

    [TestCase(3, false)] [TestCase(3, true)]
    [TestCase(128, false)] [TestCase(128, true)]
    public async Task SuccessiveReadsAndFinalUnterminatedRecord_HaveEquivalentSyncAsyncOutputs(int width, bool header)
    {
        var values = Enumerable.Range(0, 4).Select(i => (i + 1).ToString().PadLeft(width, '0')).ToArray();
        var content = (header ? "Value".PadRight(width + 3) + "\r\n" : "")
            + string.Join("\r\n", values.Select(value => value + "é  "));
        var format = new FixedWidthReaderBuilder().WithLineTerminator("\r\n").WithHeader(header)
            .WithParserOptimizations(new(BufferSize: 16)).WithPositionTracking()
            .WithField("Number", 0, width, FixedWidthPadding.Left, '0').WithField("Text", width, 3)
            .Build();
        using var sync = format.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        await using var async = format.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        var retained = new List<string>();
        for (var i = 0; i < values.Length; i++)
        {
            Assert.That(sync.Read(), Is.True);
            Assert.That(await async.ReadAsync(), Is.True);
            Assert.That(sync.GetRawString(0), Is.EqualTo(values[i]));
            Assert.That(sync.GetRawString(0), Is.EqualTo(async.GetRawString(0)));
            Assert.That(sync.GetInt32(0), Is.EqualTo(i + 1));
            Assert.That(sync.GetInt32(0), Is.EqualTo(async.GetInt32(0)));
            Assert.That(sync.GetString(1), Is.EqualTo("é"));
            Assert.That(sync.GetString(1), Is.EqualTo(async.GetString(1)));
            Assert.That(sync.GetName(0), Is.EqualTo(async.GetName(0)));
            Assert.That(sync.CurrentPosition, Is.EqualTo(async.CurrentPosition));
            retained.Add(sync.GetRawString(0));
        }
        Assert.That(sync.Read(), Is.False);
        Assert.That(await async.ReadAsync(), Is.False);
        Assert.That(retained, Is.EqualTo(values), "Materialized strings survive subsequent reads");
    }
}
