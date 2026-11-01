using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.WebLogs.Testing;

public class CurrentRecordViewTests
{
    [Test]
    [Combinatorial]
    public void Fields_PreserveBorrowedStorageAndOwnedStrings(
        [Values(WebLogFormat.Common, WebLogFormat.Combined, WebLogFormat.W3cExtended)] WebLogFormat format,
        [Values] bool asynchronous)
    {
        using var reader = CreateReader(format, Input(format, "/first", "/longer"));
        var wrapper = typeof(BaseRawRecord<WebLogProfile>).GetField("_record", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(Read(reader, asynchronous), Is.True);
        string retained = reader.GetString(4);
        Assert.That(reader.GetInt32(5), Is.EqualTo(200));
        Assert.That(reader.GetRawString(6), Is.EqualTo("1024"));
        Assert.That(wrapper.GetValue(reader), Is.Null);
        Assert.That(Read(reader, !asynchronous), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo("/longer"));
        Assert.That(retained, Is.EqualTo("/first"));
        Assert.That(wrapper.GetValue(reader), Is.Null);
        Assert.That(Read(reader, asynchronous), Is.False);
    }

    [Test]
    [Combinatorial]
    public void RawReader_HasABoundedAllocationBudget(
        [Values(WebLogFormat.Common, WebLogFormat.Combined, WebLogFormat.W3cExtended)] WebLogFormat format,
        [Values] bool asynchronous)
    {
        const int rows = 2000;
        var data = Encoding.UTF8.GetBytes(Input(format, Enumerable.Repeat("/path", rows).ToArray()));
        int ReadAll()
        {
            using var reader = CreateReader(format, data);
            int count = 0;
            // MemoryStream completes async I/O inline: allocation accounting stays on this thread.
            while (Read(reader, asynchronous)) count++;
            return count;
        }

        for (int i = 0; i < 3; i++) ReadAll();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = ReadAll();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(count, Is.EqualTo(rows));
        // Includes initialization and framing; rejects an 80-byte wrapper on every row.
        Assert.That(allocated, Is.LessThan(32L * rows), $"Allocated {allocated} bytes.");
    }

    [TestCase(WebLogFormat.Common)]
    [TestCase(WebLogFormat.Combined)]
    [TestCase(WebLogFormat.W3cExtended)]
    public async Task PublicAsyncSource_PreservesBorrowedMetadataAndOwnedValues(WebLogFormat format)
    {
        using var text = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(Input(format, "/first", "/longer"))));
        using var source = new WebLogRecordSource(text, new WebLogProfile(format));
        var first = await source.ReadAsync();
        var fields = first.Record.FieldSpans;
        string retained = first.Record.Slice(4).ToString();
        var second = await source.ReadAsync();
        Assert.That(second.Record, Is.Not.SameAs(first.Record));
        Assert.That(second.Record.FieldSpans, Is.SameAs(fields));
        Assert.That(second.Record.Slice(4).ToString(), Is.EqualTo("/longer"));
        Assert.That(retained, Is.EqualTo("/first"));
        Assert.That((await source.ReadAsync()).IsEndOfFile, Is.True);
    }

    private static bool Read(WebLogDataReader reader, bool asynchronous)
        => asynchronous ? reader.ReadAsync().GetAwaiter().GetResult() : reader.Read();

    private static WebLogDataReader CreateReader(WebLogFormat format, string input)
        => CreateReader(format, Encoding.UTF8.GetBytes(input));

    private static WebLogDataReader CreateReader(WebLogFormat format, byte[] data)
        => new(new MemoryStream(data), new WebLogProfile(format,
            parserOptimizations: new(BufferSize: 4096, ReadAhead: false, RowCountAtStart: false)));

    private static string Input(WebLogFormat format, params string[] requests)
    {
        string prefix = format == WebLogFormat.W3cExtended
            ? "#Fields: host identity user timestamp request status bytes\n" : "";
        return prefix + string.Concat(requests.Select(request => format == WebLogFormat.W3cExtended
            ? $"127.0.0.1 - frank 2000-10-10T13:55:36-07:00 {request} 200 1024\n"
            : $"127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"{request}\" 200 1024" +
                (format == WebLogFormat.Combined ? " \"-\" \"Agent/1.0\"\n" : "\n")));
    }
}
