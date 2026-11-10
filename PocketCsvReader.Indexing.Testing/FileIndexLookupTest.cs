using System.Data;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;
using PocketCsvReader.KeyValue.Configuration;
using PocketCsvReader.Ndjson.Configuration;
using PocketCsvReader.SecurityLogs.Configuration;
using PocketCsvReader.WebLogs;

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
    public void W3cWithHeaderDirectivesUsesTheOptionalScan()
    {
        var path = Write("w3c.log", "#Fields: sc-status cs-uri-stem\n3 /a\n2 /b\n1 /c\n2 /d\n");
        var reader = new W3cExtendedLogReader();
        var index = new SparseIndex(new IndexKey<int>("sc-status"), 1);
        Assert.Throws<NotSupportedException>(() => reader.BuildIndexes(path, index));
        Assert.Throws<NotSupportedException>(() => reader.WithIndexedLookup(path, index, 2));
        using var rows = reader.WithIndexedLookup(path, index.WithOptionalIndex(), 2);
        Assert.That(ReadStrings(rows, "cs-uri-stem"), Is.EqualTo(new[] { "/b", "/d" }));
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

    private static string[] ReadStrings(IDataReader rows, string column)
    {
        var values = new List<string>();
        while (rows.Read())
            values.Add(rows.GetString(rows.GetOrdinal(column)));
        return [.. values];
    }
}
