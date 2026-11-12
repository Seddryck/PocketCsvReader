using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class BitmapIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-bitmap-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void EqualityMembershipAndConjunctionMatchTheOptionalScan()
    {
        var path = Write("orders.csv", "Id,Status,Region\na,shipped,EU\nb,delivered,US\nc,delivered,eu\n"
            + "d,returned,EU\ne,SHIPPED,eu\nf,NULL,EU\ng,delivered\nh,pending,NULL\ni\nj,Shipped,EU\n");
        var builder = Builder().WithDialect(d => d.WithNullSequence("NULL"));
        var query = builder.FromDirectory(_directory, "*.csv").WithPartitions(p => p.HiveStyle()).Build();
        var index = CategoricalIndex();
        var shipped = BitmapPredicate.Equal("Status", "shipped");
        var fulfilled = BitmapPredicate.In("Status", "delivered", "SHIPPED", "delivered");
        var eu = BitmapPredicate.Equal("Region", "EU");
        (BitmapPredicate Predicate, string[] Ids)[] cases =
        [
            (shipped, ["a", "e", "j"]),
            (fulfilled, ["a", "b", "c", "e", "g", "j"]),
            (fulfilled.And(eu), ["a", "c", "e", "j"]),
            (eu.And(fulfilled).And(shipped), ["a", "e", "j"]),
            (BitmapPredicate.In("Status", "absent", "NULL"), []),
            (BitmapPredicate.In<string>("Status"), [])
        ];

        foreach (var (predicate, expected) in cases)
        {
            using var scan = query.WithIndexedLookup(index.WithOptionalIndex(), predicate).ToDataReader();
            Assert.That(Collect(scan), Is.EqualTo(expected));
        }
        Assert.That(File.Exists(path + ".bitmap.idx"), Is.False);

        query.BuildIndexes(index);
        var reloaded = builder.FromDirectory(_directory, "*.csv").WithPartitions(p => p.HiveStyle()).Build();
        foreach (var (predicate, expected) in cases)
        {
            using var rows = reloaded.WithIndexedLookup(CategoricalIndex(), predicate).ToDataReader();
            Assert.That(Collect(rows), Is.EqualTo(expected));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IntersectsBitmapsBeforeParsingAnySelectedRecords(bool emitBom)
    {
        const string data = "Id,Status,Region,Note\na,shipped,EU,\"first\nmultiline\"\nbadx,shipped,US,ok\n"
            + "c,pending,EU,ok\nd,delivered,EU,\"last\nmultiline\"";
        var encoding = new UTF8Encoding(emitBom);
        var path = Write("combined.csv", data, encoding);
        var csv = Builder().WithParserOptimizations(new() { BufferSize = 5 }).Build();
        var index = CategoricalIndex();
        csv.BuildIndexes(path, index);

        // Preserve the source stamps to detect if the reader parses a disjoint row.
        var stamp = File.GetLastWriteTimeUtc(path);
        Write("combined.csv", data.Replace("badx", "b,a,", StringComparison.Ordinal), encoding);
        File.SetLastWriteTimeUtc(path, stamp);
        var fulfilled = BitmapPredicate.In("Status", "shipped", "delivered");
        using (var singleColumn = csv.WithIndexedLookup(path, index, fulfilled))
            Assert.Throws<InvalidDataException>(() => Collect(singleColumn));

        var combined = fulfilled.And(BitmapPredicate.Equal("Region", "EU"));
        using (var rows = csv.WithIndexedLookup(path, index, combined))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "a", "d" }));
        await using (var rows = csv.WithIndexedLookup(path, index, combined))
        {
            var matches = new List<string>();
            while (await rows.ReadAsync())
                matches.Add(rows.GetString(0));
            Assert.That(matches, Is.EqualTo(new[] { "a", "d" }));
        }

        var disjoint = BitmapPredicate.Equal("Status", "pending").And(BitmapPredicate.Equal("Region", "US"));
        using var none = csv.WithIndexedLookup(path, index, disjoint);
        Assert.That(none.Read(), Is.False);
    }

    [Test]
    public void HeaderlessTypedColumnsPreserveRowsAtBitmapWordBoundaries()
    {
        var special = new HashSet<int> { 0, 63, 64, 65, 127, 128, 129 };
        var data = Enumerable.Range(0, 130).Select(row =>
        {
            var date = special.Contains(row) ? "15/05/2026" : "01/01/2025";
            var region = row is 64 or 127 ? 9 : 7;
            return string.Create(CultureInfo.InvariantCulture, $"{row},{date},{region}");
        });
        var path = Write("headerless.csv", string.Join('\n', data));
        var csv = Builder().WithDialect(d => d.WithoutHeader()).Build();
        var index = new BitmapIndex(new IndexKey<DateOnly>(1, format: "dd/MM/yyyy"), new IndexKey<int>(2));
        var predicate = BitmapPredicate.Equal(1, new DateOnly(2026, 5, 15)).And(BitmapPredicate.Equal(2, 7));
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, predicate);
        Assert.That(Collect(rows), Is.EqualTo(new[] { "0", "63", "65", "128", "129" }));
    }

    [Test]
    public void PartitionsRowFiltersAndPaginationPreserveSourceRowNumbers()
    {
        var first = Write("first.csv", "Id,Status,Region\na,shipped,US\n# comment\nb,pending,EU\n");
        var selected = Write("selected.csv", "Id,Status,Region\na,shipped,US\n# before\nb,shipped,EU\n"
            + "c,delivered,EU\nd,returned,EU\n# between\ne,delivered,EU");
        var rejected = Write("rejected.csv", "Id,Status,Region\nrejected,shipped,EU\n");
        var query = Builder().WithDialect(d => d.WithCommentChar('#')).WithRowNumber().WithSkip(1).WithTake(1)
            .FromFiles([first, selected, rejected]).WithPartitions(p => p.FromPath(path => path != rejected)).Build();
        var index = CategoricalIndex();
        query.WithPartitionFilter(keep => keep).WithFilter("Id", "never").BuildIndexes(index);
        Assert.That(File.Exists(rejected + ".bitmap.idx"), Is.False);

        var predicate = BitmapPredicate.In("Status", "shipped", "delivered").And(BitmapPredicate.Equal("Region", "EU"));
        using var rows = query.WithIndexedLookup(index, predicate).WithPartitionFilter(keep => keep)
            .WithFilter("Id", id => !id.SequenceEqual("c")).ToDataReader();
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.GetString(1), Is.EqualTo("e"));
        Assert.That(rows.GetInt32(0), Is.EqualTo(7));
        Assert.That(rows.Read(), Is.False);
    }

    [TestCase("")]
    [TestCase("Id,Status,Region\n")]
    [TestCase("Id,Status,Region\na,NULL,NULL\nb")]
    public void EmptySelectionsCanBeReloaded(string content)
    {
        var path = Write("empty.csv", content);
        var csv = Builder().WithDialect(d => d.WithHeader(content.Length != 0).WithNullSequence("NULL")).Build();
        var index = new BitmapIndex(new IndexKey<string>(1), new IndexKey<string>(2));
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, BitmapPredicate.Equal(1, "shipped").And(BitmapPredicate.Equal(2, "EU")));
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void MissingSidecarRequiresOptInAndOptionalLookupDoesNotCreateIt()
    {
        var path = Write("missing.csv", "Id,Status,Region\na,shipped,EU\nb,returned,EU\n");
        var csv = Builder().Build();
        var index = CategoricalIndex().WithOptionalIndex();
        var predicate = BitmapPredicate.Equal("Status", "shipped");
        using (var strict = csv.WithIndexedLookup(path, index.WithRequiredIndex(), predicate))
            Assert.Throws<FileNotFoundException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index, predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));
        Assert.That(File.Exists(path + ".bitmap.idx"), Is.False);
    }

    [TestCase("checksum")]
    [TestCase("version")]
    [TestCase("unreadable")]
    [TestCase("length")]
    [TestCase("timestamp")]
    [TestCase("key order")]
    [TestCase("comparison")]
    [TestCase("settings")]
    public void SupportedSourcesRejectInvalidSidecarsUnderEitherPolicy(string failure)
    {
        var path = Write("invalid.csv", "Id,Status,Region\na,shipped,EU\nb,returned,EU\n");
        var csv = Builder().Build();
        var index = CategoricalIndex();
        var predicate = BitmapPredicate.Equal("Status", "shipped");
        csv.BuildIndexes(path, index);
        var sidecar = path + ".bitmap.idx";
        var stamp = File.GetLastWriteTimeUtc(path);
        switch (failure)
        {
            case "checksum":
                var payload = File.ReadAllBytes(sidecar);
                payload[^1] ^= 1;
                File.WriteAllBytes(sidecar, payload);
                break;
            case "version":
                var version = File.ReadAllBytes(sidecar);
                version[39] = (byte)'2';
                SHA256.HashData(version.AsSpan(32)).CopyTo(version, 0);
                File.WriteAllBytes(sidecar, version);
                break;
            case "unreadable":
                using (File.Open(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    AssertRejected<IOException>();
                return;
            case "length":
                File.AppendAllText(path, "c,delivered,EU\n");
                File.SetLastWriteTimeUtc(path, stamp);
                break;
            case "timestamp":
                File.SetLastWriteTimeUtc(path, stamp.AddHours(-1));
                break;
            case "key order":
                index = new BitmapIndex(index.Keys.Reverse().ToArray());
                break;
            case "comparison":
                index = new BitmapIndex(new IndexKey<string>("Status"), index.Keys[1]);
                break;
            case "settings":
                csv = Builder().WithDialect(d => d.WithNullSequence("shipped")).Build();
                break;
        }
        AssertRejected<InvalidDataException>();

        void AssertRejected<TException>()
            where TException : Exception
        {
            using var strict = csv.WithIndexedLookup(path, index, predicate);
            Assert.Throws<TException>(() => strict.Read());
            using var optional = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate);
            Assert.Throws<TException>(() => optional.Read());
        }
    }

    [Test]
    public void Utf16AndSharedHeadersRequireOptionalScan()
    {
        const string data = "Id,Status,Region\na,shipped,EU\nb,returned,EU\n";
        var path = Write("utf16.csv", data, Encoding.Unicode);
        var csv = Builder().Build();
        var index = CategoricalIndex();
        var predicate = BitmapPredicate.Equal("Status", "shipped");
        Assert.Throws<NotSupportedException>(() => csv.BuildIndexes(path, index));
        using (var strict = csv.WithIndexedLookup(path, index, predicate))
            Assert.Throws<NotSupportedException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));

        var utf8 = Write("shared-header.csv", data);
        var query = Builder().WithDialect(d => d.WithHeaderRepeat(false)).FromFiles([utf8])
            .WithPartitions(p => p.FromPath(_ => true)).Build();
        Assert.Throws<NotSupportedException>(() => query.BuildIndexes(index));
        Assert.Throws<NotSupportedException>(() => query.WithIndexedLookup(index, predicate));
        using var shared = query.WithIndexedLookup(index.WithOptionalIndex(), predicate).ToDataReader();
        Assert.That(Collect(shared), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void RuntimeParserIsUsedForBuildAndLookupAndExplicitFormatOverridesIt()
    {
        var path = Write("dates.csv", "Id,Date,Region\na,15/05/2026,EU\nb,01/01/2025,EU\nc,15/05/2026,US\n");
        var csv = Builder().WithParsers(p => p.WithParser<DateOnly>(static text =>
            DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture))).Build();
        var region = new IndexKey<string>("Region");
        var index = new BitmapIndex(new IndexKey<DateOnly>("Date"), region);
        var predicate = BitmapPredicate.Equal("Date", new DateOnly(2026, 5, 15)).And(BitmapPredicate.Equal("Region", "EU"));
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));
        csv.BuildIndexes(path, index);
        using (var indexed = csv.WithIndexedLookup(path, index, predicate))
            Assert.That(Collect(indexed), Is.EqualTo(new[] { "a" }));

        var plain = Builder().Build();
        using (var incompatible = plain.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.Throws<InvalidDataException>(() => incompatible.Read());
        var explicitFormat = new BitmapIndex(new IndexKey<DateOnly>("Date", format: "dd/MM/yyyy"), region);
        csv.BuildIndexes(path, explicitFormat);
        using var explicitRows = plain.WithIndexedLookup(path, explicitFormat, predicate);
        Assert.That(Collect(explicitRows), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void OpenReaderRequiresOptionalLookupAndTransfersOwnership()
    {
        using var stream = new MemoryStream("Id,Date,Region\na,01/01/2025,EU\nb\nc,15/05/2026,EU\n"u8.ToArray());
        var input = Builder().Build().ToDataReader(stream);
        var index = new BitmapIndex(new IndexKey<DateOnly>("Date", format: "dd/MM/yyyy"), new IndexKey<string>(2));
        var predicate = BitmapPredicate.Equal("Date", new DateOnly(2026, 5, 15)).And(BitmapPredicate.Equal(2, "EU"));
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index, predicate));
        using (var rows = input.WithIndexedLookup(index.WithOptionalIndex(), predicate))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "c" }));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void DefinitionsAndPredicatesAreValidatedBeforeOpeningFiles()
    {
        var csv = Builder().Build();
        var index = CategoricalIndex();
        Assert.Throws<ArgumentException>(() => new BitmapIndex());
        Assert.Throws<ArgumentException>(() => new BitmapIndex(index.Keys[0], index.Keys[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapPredicate.Equal(-1, 1));
        Assert.Throws<ArgumentException>(() => BitmapPredicate.In<string>("Status", "shipped", null!));
        Assert.Throws<ArgumentException>(() => csv.WithIndexedLookup("missing.csv", index, BitmapPredicate.Equal("Status", 7)));
        Assert.Throws<ArgumentException>(() => csv.WithIndexedLookup("missing.csv", index, BitmapPredicate.Equal("Unknown", "shipped")));
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private static BitmapIndex CategoricalIndex() => new(
        new IndexKey<string>("Status", comparison: StringComparison.OrdinalIgnoreCase),
        new IndexKey<string>("Region", comparison: StringComparison.OrdinalIgnoreCase));

    private string Write(string name, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, encoding ?? Encoding.UTF8);
        return path;
    }

    private static List<string> Collect(IDataReader rows)
    {
        var result = new List<string>();
        while (rows.Read())
            result.Add(rows.GetString(0));
        return result;
    }
}
