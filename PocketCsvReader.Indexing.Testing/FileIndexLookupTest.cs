using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;
using PocketCsvReader.KeyValue.Configuration;
using PocketCsvReader.Ndjson.Configuration;
using PocketCsvReader.SecurityLogs.Configuration;
using PocketCsvReader.WebLogs;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class FileIndexLookupTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-file-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task SortedNdjsonSeeksAfterUtf8AndReturnsDuplicatesAcrossCheckpoints()
    {
        const string first = "{\"date\":\"2026-01-01\",\"id\":\"é雪😀\"}\n";
        const string second = "{\"id\":\"a\",\"date\":\"2026-05-17\"}\n";
        const string third = "{\"date\":\"2026-05-17\",\"id\":\"b\"}\n";
        const string fourth = "{\"id\":\"c\",\"date\":\"2026-05-17\"}\n";
        const string last = "{\"date\":\"2026-12-31\",\"id\":\"d\"}";
        string path = Write("orders.ndjson", first + second + third + fourth + last, bom: true);
        int bufferSize = first.IndexOf("😀", StringComparison.Ordinal) + 1;
        var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithParserOptimizations(new(BufferSize: bufferSize)).Build();
        var index = new SparseIndex(new IndexKey<DateOnly>("date"), 2);
        reader.BuildIndexes(path, index);

        var thirdOffset = Encoding.UTF8.GetByteCount(first + second) + Encoding.UTF8.Preamble.Length;
        Assert.That(File.ReadAllLines(path + ".sparse.idx"), Does.Contain(
            "checkpoint," + thirdOffset.ToString(CultureInfo.InvariantCulture) + ",2026-05-17"));
        using (var rows = reader.WithIndexedLookup(path, index, new DateOnly(2026, 5, 17)))
            Assert.That(ReadStrings(rows, "id"), Is.EqualTo(new[] { "a", "b", "c" }));

        // Preserve the file stamp and poison an early key. A scan from the
        // beginning would try to parse this value and fail.
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, first.Replace("2026-01-01", "xxxx-xx-xx") + second + third + fourth + last,
            new UTF8Encoding(true));
        File.SetLastWriteTimeUtc(path, stamp);

        await using var found = reader.WithIndexedLookup(path, index, new DateOnly(2026, 12, 31));
        Assert.That(await found.ReadAsync(), Is.True);
        Assert.That(found.GetString(found.GetOrdinal("id")), Is.EqualTo("d"));
        Assert.That(await found.ReadAsync(), Is.False);
    }

    [Test]
    public void NdjsonReusesTheRegisteredStringDelegateForBuildAndLookup()
    {
        var path = Write("orders.ndjson", "{\"date\":\"31/01/2026\",\"id\":\"a\"}\n"
            + "{\"date\":\"17/05/2026\",\"id\":\"b\"}\n{\"date\":\"01/12/2026\",\"id\":\"c\"}\n");
        var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.WithParser<DateOnly>((string text) =>
                DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture))).Build();
        var index = new SparseIndex(new IndexKey<DateOnly>("date"), 1);
        var date = new DateOnly(2026, 5, 17);

        using (var scan = reader.WithIndexedLookup(path, index.WithOptionalIndex(), date))
            Assert.That(ReadStrings(scan, "id"), Is.EqualTo(new[] { "b" }));

        reader.BuildIndexes(path, index);
        using var indexed = reader.WithIndexedLookup(path, index, date);
        Assert.That(ReadStrings(indexed, "id"), Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public void CapturingRuntimeParserCanScanButCannotUseAPersistedIndex()
    {
        var path = Write("orders.ndjson", "{\"date\":\"2026-05-17\"}\n");
        var format = "yyyy-MM-dd";
        var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.WithParser<DateOnly>((ReadOnlySpan<char> text) =>
                DateOnly.ParseExact(text, format, CultureInfo.InvariantCulture))).Build();
        var index = new SparseIndex(new IndexKey<DateOnly>("date"), 1);
        var date = new DateOnly(2026, 5, 17);

        Assert.Throws<NotSupportedException>(() => reader.BuildIndexes(path, index));
        Assert.That(File.Exists(path + ".sparse.idx"), Is.False);
        using (var scan = reader.WithIndexedLookup(path, index.WithOptionalIndex(), date))
            Assert.That(ReadStrings(scan, "date"), Is.EqualTo(new[] { "2026-05-17" }));

        new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build().BuildIndexes(path, index);
        Assert.Throws<NotSupportedException>(() => reader.WithIndexedLookup(path, index.WithOptionalIndex(), date));
    }

    [Test]
    public void RequiredIsDefaultAndOptionalScansAnUnsortedNdjsonFile()
    {
        var path = Write("unsorted.ndjson", "{\"key\":4}\n{\"key\":2}\n{\"key\":3}\n{\"key\":2}\n");
        var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build();
        var required = new SparseIndex(new IndexKey<int>("key"), 1);
        var optional = required.WithOptionalIndex();

        Assert.Throws<FileNotFoundException>(() => reader.WithIndexedLookup(path, required, 2));
        Assert.Throws<FileNotFoundException>(() => reader.WithIndexedLookup(path, optional.WithRequiredIndex(), 2));
        using (var rows = reader.WithIndexedLookup(path, optional, 2))
            Assert.That(ReadStrings(rows, "key"), Is.EqualTo(new[] { "2", "2" }));

        Assert.Throws<InvalidDataException>(() => reader.BuildIndexes(path, optional));
        Assert.That(File.Exists(path + ".sparse.idx"), Is.False);
    }

    [Test]
    public void SortedNdjsonUsesTheSameFiltersAndWindowWithOrWithoutAnIndex()
    {
        var path = Write("window.ndjson",
            "{\"key\":0,\"id\":\"a\"}\n{\"key\":1,\"id\":\"b\"}\n{\"key\":2,\"id\":\"c\"}\n"
            + "{\"key\":2,\"id\":\"d\"}\n{\"key\":2,\"id\":\"e\"}\n{\"key\":3,\"id\":\"f\"}\n");
        var reader = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithFilter("id", text => !text.SequenceEqual("c")).WithSkip(1).WithTake(1).Build();
        var index = new SparseIndex(new IndexKey<int>("key"), 1);
        using (var scan = reader.WithIndexedLookup(path, index.WithOptionalIndex(), 2))
            Assert.That(ReadStrings(scan, "id"), Is.EqualTo(new[] { "e" }));

        reader.BuildIndexes(path, index);
        Assert.That(File.ReadAllLines(path + ".sparse.idx"), Does.Contain("metadata,dataRowCount,6"));
        using (var indexed = reader.WithIndexedLookup(path, index, 2))
            Assert.That(ReadStrings(indexed, "id"), Is.EqualTo(new[] { "e" }));
        var differentDialect = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\r\n")).Build();
        Assert.Throws<InvalidDataException>(() => differentDialect.WithIndexedLookup(path, index.WithOptionalIndex(), 2));
        File.AppendAllText(path, "{\"key\":4,\"id\":\"g\"}\n");
        Assert.Throws<InvalidDataException>(() => reader.WithIndexedLookup(path, index.WithOptionalIndex(), 2));
    }

    [Test]
    public void CsvFlatFileReaderUsesTheSameSidecarAsPartitionedCsv()
    {
        var path = Write("orders.csv", "key,id\n1,a\n2,b\n2,c\n3,d\n");
        var reader = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n")).Build();
        var index = new SparseIndex(new IndexKey<int>("key"), 1);
        reader.BuildIndexes(path, index);
        using var rows = reader.WithIndexedLookup(path, index.WithRequiredIndex(), 2);
        Assert.That(ReadStrings(rows, "id"), Is.EqualTo(new[] { "b", "c" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CsvReadsJoinedHeadersBeforeSeekingInSingleAndPartitionedFiles(bool asynchronous)
    {
        const string header = "Order,Order\r\nKey,Name\r\n";
        const string remaining = "200,before\r\n300,first\r\n300,second\r\n400,after";
        var path = Write("headers.csv", header + "100,early\r\n" + remaining, bom: true);
        var single = Builder(repeat: false).Build();
        var index = new SparseIndex(new IndexKey<int>("Order_Key"), 1);
        single.BuildIndexes(path, index);
        ReplaceWithoutChangingStamp(path, header + "xxx,early\r\n" + remaining);

        await using (var rows = single.WithIndexedLookup(path, index, 300))
            Assert.That(await ReadStringsAsync(rows, "Order_Name", asynchronous), Is.EqualTo(new[] { "first", "second" }));

        var partitioned = Builder(repeat: true).FromFiles(new[] { FileSource.Partitioned(path, true) }).Build();
        await using var batch = partitioned.WithIndexedLookup(index, 300).ToDbDataReader();
        Assert.That(await ReadStringsAsync(batch, "Order_Name", asynchronous), Is.EqualTo(new[] { "first", "second" }));

        static CsvReaderBuilder Builder(bool repeat) => new CsvReaderBuilder().WithDialect(d => d.WithHeader()
            .WithHeaderRows([1, 2]).WithHeaderJoin("_").WithHeaderRepeat(repeat).WithLineTerminator("\r\n"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FixedWidthReadsPaddedHeaderBeforeSeeking(bool asynchronous)
    {
        const string header = "KeyName \r\n";
        const string remaining = "200base \r\n300one  \r\n300two  \r\n400after";
        var path = Write("header.fixed", header + "100early\r\n" + remaining, bom: true);
        var reader = new FixedWidthReaderBuilder().WithHeader().WithLineTerminator("\r\n")
            .WithField("configured-key", 0, 3).WithField("configured-name", 3, 5).Build();
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        reader.BuildIndexes(path, index);
        ReplaceWithoutChangingStamp(path, header + "xxxearly\r\n" + remaining);

        await using var rows = reader.WithIndexedLookup(path, index, 300);
        Assert.That(await ReadStringsAsync(rows, "Name", asynchronous), Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void FixedWidthAndKeyValueFormatsUseTheSharedSeekPath()
    {
        var fixedWidth = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n").WithField("key", 0, 3).WithField("id", 3, 1).Build();
        var logfmt = new LogfmtReaderBuilder().Build();
        var index = new SparseIndex(new IndexKey<int>("key"), 1);

        AssertSeek(fixedWidth, "data.fixed", "1  a\n2  b\n2  c\n3  d\n", index, "id");
        AssertSeek(logfmt, "data.logfmt", "key=1 id=a\nid=b key=2\nkey=2 id=c\nkey=3 id=d\n", index, "id");
        AssertSeek(new LtsvReaderBuilder().Build(), "data.ltsv",
            "key:1\tid:a\nid:b\tkey:2\nkey:2\tid:c\nkey:3\tid:d\n", index, "id");
    }

    [Test]
    public void StatelessWebAndSecurityLogFormatsUseTheSharedSeekPath()
    {
        var web = new CommonLogReader();
        const string common = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET /index HTTP/1.0\" ";
        var webIndex = new SparseIndex(new IndexKey<int>("StatusCode"), 1);
        AssertSeek(web, "access.log", common + "100 10\n" + common + "200 20\n" + common + "200 21\n" + common + "300 30\n",
            webIndex, "ResponseBytes", "20", "21", value: 200);
        const string suffix = " \"-\" \"agent\"\n";
        AssertSeek(new CombinedLogReader(), "combined.log",
            common + "100 10" + suffix + common + "200 20" + suffix + common + "200 21" + suffix + common + "300 30" + suffix,
            webIndex, "ResponseBytes", "20", "21", value: 200);

        var cef = new CefReaderBuilder().WithAttributes("msg").Build();
        const string prefix = "CEF:0|Acme|Firewall|1.0|42|Message|";
        var securityIndex = new SparseIndex(new IndexKey<int>("Severity"), 1);
        AssertSeek(cef, "events.cef", prefix + "1|msg=a\n" + prefix + "2|msg=b\n" + prefix + "2|msg=c\n" + prefix + "3|msg=d\n",
            securityIndex, "msg");
        const string leef = "LEEF:1.0|Acme|Firewall|1.0|42|";
        AssertSeek(new LeefReaderBuilder().WithAttributes("sev", "msg").Build(), "events.leef",
            leef + "sev=1\tmsg=a\n" + leef + "sev=2\tmsg=b\n" + leef + "sev=2\tmsg=c\n" + leef + "sev=3\tmsg=d\n",
            new SparseIndex(new IndexKey<int>("sev"), 1), "msg");
    }

    [Test]
    public void W3cWithoutASidecarUsesTheOptionalScan()
    {
        var path = Write("w3c.log", "#Fields: sc-status cs-uri-stem\n3 /a\n2 /b\n1 /c\n2 /d\n");
        var reader = new W3cExtendedLogReader();
        var index = new SparseIndex(new IndexKey<int>("sc-status"), 1);
        Assert.Throws<FileNotFoundException>(() => reader.WithIndexedLookup(path, index, 2));
        using var rows = reader.WithIndexedLookup(path, index.WithOptionalIndex(), 2);
        Assert.That(ReadStrings(rows, "cs-uri-stem"), Is.EqualTo(new[] { "/b", "/d" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task W3cReadsTheHeaderAndSeeksPastEarlierRows(bool asynchronous)
    {
        const string header = "#Software: café\r\n#Fields: cs-uri-stem sc-status\r\n";
        const string remaining = "/before 200\r\n/first 300\r\n#Fields: cs-uri-stem sc-status\r\n/second 300\r\n/after 400\r\n";
        var path = Write("indexed.log", header + "/early 100\r\n" + remaining, bom: true);
        var reader = new W3cExtendedLogReaderBuilder().WithParserOptimizations(new(BufferSize: 5)).Build();
        var index = new SparseIndex(new IndexKey<int>("sc-status"), 1);
        reader.BuildIndexes(path, index);

        // Leave an invalid key before the selected checkpoint with the same file stamp.
        // A scan that reads data rows between the header and checkpoint would fail.
        ReplaceWithoutChangingStamp(path, header + "/early xxx\r\n" + remaining);

        await using var rows = reader.WithIndexedLookup(path, index, 300);
        Assert.That(await ReadStringsAsync(rows, "cs-uri-stem", asynchronous), Is.EqualTo(new[] { "/first", "/second" }));
    }

    private void AssertSeek<TProfile, TDataReader>(FlatFileReader<TProfile, TDataReader> reader,
        string name, string contents, SparseIndex index, string column, string first = "b", string second = "c", int value = 2)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        var path = Write(name, contents);
        reader.BuildIndexes(path, index);
        using var rows = reader.WithIndexedLookup(path, index, value);
        Assert.That(ReadStrings(rows, column), Is.EqualTo(new[] { first, second }));
    }

    private string Write(string name, string contents, bool bom = false)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents, new UTF8Encoding(bom));
        return path;
    }

    private static void ReplaceWithoutChangingStamp(string path, string content)
    {
        // Keep both source metadata values so the test can detect an accidental sequential scan.
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, content, new UTF8Encoding(true));
        File.SetLastWriteTimeUtc(path, stamp);
    }

    private static async Task<string[]> ReadStringsAsync(DbDataReader rows, string column, bool asynchronous)
    {
        var values = new List<string>();
        while (asynchronous ? await rows.ReadAsync() : rows.Read())
            values.Add(rows.GetString(rows.GetOrdinal(column)));
        return [.. values];
    }

    private static string[] ReadStrings(IDataReader rows, string column)
    {
        var values = new List<string>();
        while (rows.Read())
            values.Add(rows.GetString(rows.GetOrdinal(column)));
        return [.. values];
    }
}
