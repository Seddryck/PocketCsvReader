using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

public class LazyClickHouseJsonColumnsTests
{
    private static readonly Schema TestSchema = new Schema.Builder()
        .Field(new Field("id", Int32Type.Default, false))
        .Field(new Field("payload", StringType.Default, true))
        .Build();

    [Test]
    public async Task DecodesAndCachesByNameOrOrdinalAfterClosingTheSource()
    {
        const string text = """{"payload":["café",null],"id":[42,43]}""";
        using var input = new AsyncOnlyInput(text, 4);
        using var stream = await new ClickHouseJsonColumnsReader(TestSchema, leaveOpen: false).ToLazyArrowStreamAsync(input);
        using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
        stream.Dispose();

        Assert.That(input.Disposed, Is.True);
        Assert.That(record.Length, Is.EqualTo(2));
        Assert.That(record.Schema, Is.SameAs(TestSchema));
        var reads = await Task.WhenAll(Task.Run(() => record.Column(0)), Task.Run(() => record.Column("ID", StringComparer.OrdinalIgnoreCase)));
        Assert.That(reads[0], Is.SameAs(reads[1]));
        Assert.That(((Int32Array)reads[0]).GetValue(1), Is.EqualTo(43));
        var payload = (StringArray)record.Column(1);
        Assert.That(payload.GetString(0), Is.EqualTo("café"));
        Assert.That(payload.IsNull(1), Is.True);
        record.Dispose();
        Assert.That(() => record.Column(0), Throws.TypeOf<ObjectDisposedException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpensFilesAndKeepsRecordsUsableAfterClosingTheInput(bool asynchronous)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"id":[1],"payload":["test"]}""");
            var reader = new ClickHouseJsonColumnsReader(TestSchema);
            using var stream = asynchronous
                ? await reader.ToLazyArrowStreamAsync(path)
                : reader.ToLazyArrowStream(path);
            using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
            stream.Dispose();
            File.Delete(path);

            Assert.That(((StringArray)record.Column("payload")).GetString(0), Is.EqualTo("test"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("utf8", false)]
    [TestCase("utf8-bom", true)]
    [TestCase("utf16-le", false)]
    [TestCase("utf16-be", true)]
    [TestCase("utf32-le", true)]
    [TestCase("utf32-be", false)]
    public async Task FileColumnRangesAccountForUnicodeAndTheBom(string encodingName, bool asynchronous)
    {
        var encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(false),
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => Encoding.Unicode,
            "utf16-be" => Encoding.BigEndianUnicode,
            "utf32-le" => Encoding.UTF32,
            "utf32-be" => new UTF32Encoding(true, true),
            _ => throw new ArgumentException("Unexpected encoding.", nameof(encodingName)),
        };
        var text = new string('é', 10_000) + "😀";
        var path = Path.GetTempFileName();
        try
        {
            var gap = new string(' ', 18_000);
            var json = "{\r\n\"payload\": [" + gap + "\"" + text + "\",null],\r\n\"id\" : [42 ," + gap + "43]}";
            File.WriteAllText(path, json, encoding);
            var reader = new ClickHouseJsonColumnsReader(TestSchema);
            using var stream = asynchronous
                ? await reader.ToLazyArrowStreamAsync(path)
                : reader.ToLazyArrowStream(path);
            using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
            stream.Dispose();

            Assert.That(((Int32Array)record.Column("id")).GetValue(1), Is.EqualTo(43));
            var payload = (StringArray)record.Column("payload");
            Assert.That(payload.GetString(0), Is.EqualTo(text));
            Assert.That(payload.IsNull(1), Is.True);
            if (OperatingSystem.IsWindows())
            {
                Assert.That(() =>
                {
                    using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }, Throws.InstanceOf<IOException>());
            }

            record.Dispose();
            using var released = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TypeErrorsAreDeferredAndCached()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("""{"payload":[42],"id":[7]}"""));
        using var stream = new ClickHouseJsonColumnsReader(TestSchema).ToLazyArrowStream(input);
        using var record = stream.ReadNextRecordAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Expected a batch.");

        Assert.That(((Int32Array)record.Column(0)).GetValue(0), Is.EqualTo(7));
        var first = Assert.Throws<InvalidDataException>(() => record.Column(1));
        Assert.That(first?.Message, Does.Contain("payload"));
        Assert.That(Assert.Throws<InvalidDataException>(() => record.Column(1)), Is.SameAs(first));
    }

    [TestCase("""{"payload":[null],"id":[7,8]}""", "Expected 1")]
    [TestCase("""{"id":[7],"payload":["\q"]}""", "escape")]
    public async Task ValidatesColumnLengthsAndJsonSyntaxWhileReading(string json, string message)
    {
        using var input = new AsyncOnlyInput(json, 8);
        using var stream = await new ClickHouseJsonColumnsReader(TestSchema).ToLazyArrowStreamAsync(input);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await stream.ReadNextRecordAsync());
        Assert.That(error?.Message, Does.Contain(message));
    }

    [Test]
    public void FirstAccessPresizesTheKnownLengthOfAnInt32Column()
    {
        const int count = 100_000;
        var schema = new Schema.Builder().Field(new Field("id", Int32Type.Default, false)).Build();
        var reader = new ClickHouseJsonColumnsReader(schema);
        using var warmInput = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":[1]}"""));
        using (var warmStream = reader.ToLazyArrowStream(warmInput))
        using (var warmRecord = warmStream.ReadNextRecordAsync().GetAwaiter().GetResult())
            _ = warmRecord?.Column(0);

        var json = """{"id":[""" + string.Join(",", Enumerable.Repeat("123456789", count)) + "]}";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var stream = reader.ToLazyArrowStream(input);
        using var record = stream.ReadNextRecordAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Expected a batch.");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var ids = (Int32Array)record.Column(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(ids.Length, Is.EqualTo(count));
        Assert.That(ids.GetValue(count - 1), Is.EqualTo(123456789));
        // Allow the pre-sized builder and final Arrow buffer plus parser overhead.
        // Geometric builder growth on this input allocates more than 1 MB.
        Assert.That(allocated, Is.LessThan(1_000_000));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public async Task FileScanAllocatesOnlyPackedBoundariesAndParserBuffers(bool asynchronous)
    {
        const int count = 100_000;
        var path = Path.GetTempFileName();
        try
        {
            var json = """{"id":[""" + string.Join(",", Enumerable.Repeat("123456789", count))
                + """],"payload":[""" + string.Join(",", Enumerable.Repeat("null", count)) + "]}";
            File.WriteAllText(path, json);
            var reader = new ClickHouseJsonColumnsReader(TestSchema);
            using (var warm = await OpenAsync())
                (await warm.ReadNextRecordAsync())?.Dispose();

            var before = GC.GetTotalAllocatedBytes(precise: true);
            using var stream = await OpenAsync();
            using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.That(record.Length, Is.EqualTo(count));
            // A byte per cell plus growth slack, parser buffers and async refills
            // fit in this budget. An Int32 per cell for both columns does not.
            Assert.That(allocated, Is.LessThan(750_000));

            ValueTask<IArrowRecordStream> OpenAsync() => asynchronous
                ? reader.ToLazyArrowStreamAsync(path)
                : ValueTask.FromResult(reader.ToLazyArrowStream(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadingWithoutColumnAccessDoesNotAllocateStoragePerCell()
    {
        const int count = 150_000;
        var json = """{"id":[""" + string.Join(",", Enumerable.Repeat("123456789", count))
            + """],"payload":[""" + string.Join(",", Enumerable.Repeat("null", count)) + "]}";
        var reader = new ClickHouseJsonColumnsReader(TestSchema);
        using var warmInput = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":[1],"payload":[null]}"""));
        using (var warmStream = reader.ToLazyArrowStream(warmInput))
            warmStream.ReadNextRecordAsync().GetAwaiter().GetResult()?.Dispose();

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var stream = reader.ToLazyArrowStream(input);
        using var record = stream.ReadNextRecordAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Expected a batch.");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(record.Length, Is.EqualTo(count));
        // A retained copy of the JSON or even one materialized Int32 column exceeds this budget.
        Assert.That(allocated, Is.LessThan(350_000));
    }
}
