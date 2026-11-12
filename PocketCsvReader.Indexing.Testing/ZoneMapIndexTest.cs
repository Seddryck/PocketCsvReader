using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class ZoneMapIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-zone-map-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public void IndexedAndOptionalRangesMatchSequentialFiltering(bool unsorted, bool includeLower, bool includeUpper)
    {
        string[] records = ["low,1", "before,99", "lower-a,100", "lower-b,100.00", "middle,125",
            "upper-a,200", "upper-b,200.00", "high,900", "null,NULL", "missing"];
        if (unsorted)
        {
            records = [records[0], records[7], records[5], records[2], records[8], records[9],
                records[4], records[6], records[3], records[1]];
        }
        var path = Write("amounts.csv", "Id,Amount\n" + string.Join('\n', records));
        var builder = Builder().WithDialect(d => d.WithNullSequence("NULL"));
        var query = builder.FromFiles([path]).WithPartitions(p => p.FromPath(_ => true)).Build();
        var index = new ZoneMapIndex(new IndexKey<decimal>("Amount"), 2);
        var range = new IndexRange<decimal>(100m, 200m, includeLower, includeUpper);

        var expected = new List<string>();
        using (var rows = query.ToDataReader())
        {
            while (rows.Read())
            {
                if (rows.FieldCount < 2 || rows.IsDBNull(1))
                    continue;
                var amount = rows.GetDecimal(1);
                if ((amount > 100m || (includeLower && amount == 100m))
                    && (amount < 200m || (includeUpper && amount == 200m)))
                    expected.Add(rows.GetString(0));
            }
        }

        using (var scan = query.WithIndexedLookup(index.WithOptionalIndex(), range).ToDataReader())
            Assert.That(Collect(scan), Is.EqualTo(expected));
        Assert.That(File.Exists(path + ".zonemap.idx"), Is.False);

        query.BuildIndexes(index);
        var reloaded = builder.FromFiles([path]).WithPartitions(p => p.FromPath(_ => true)).Build();
        using var indexed = reloaded.WithIndexedLookup(new ZoneMapIndex(new IndexKey<decimal>("Amount"), 2), range)
            .ToDataReader();
        Assert.That(Collect(indexed), Is.EqualTo(expected));
        using var equality = reloaded.WithIndexedLookup(index, 200m).ToDataReader();
        Assert.That(Collect(equality), Is.EqualTo(new[] { "upper-a", "upper-b" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectiveLookupDoesNotParseDisjointBlocks(bool emitBom)
    {
        const string data = "Id,Key\nbefore,1\nnear,11\nbadx,0\nlowx,2\nafter,12\nlast,15\nlow,1\nhigh,90";
        var path = Path.Combine(_directory, "pruning.csv");
        var encoding = new UTF8Encoding(emitBom);
        File.WriteAllText(path, data, encoding);
        var csv = Builder().WithParserOptimizations(new() { BufferSize = 5 }).Build();
        var index = new ZoneMapIndex(new IndexKey<int>("Key"), 2);
        csv.BuildIndexes(path, index);

        // Keep the stamps unchanged to observe whether the parser visits a disjoint block.
        // The replacement record has the same byte length and too many fields.
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, data.Replace("badx,0", "b,a,d,", StringComparison.Ordinal), encoding);
        File.SetLastWriteTimeUtc(path, stamp);
        using (var sequential = csv.ToDataReader(path))
            Assert.Throws<InvalidDataException>(() => Collect(sequential));

        var range = new IndexRange<int>(10, 15);
        using (var sync = csv.WithIndexedLookup(path, index, range))
            Assert.That(Collect(sync), Is.EqualTo(new[] { "near", "after", "last" }));
        await using var asyncRows = csv.WithIndexedLookup(path, index, range);
        var matches = new List<string>();
        while (await asyncRows.ReadAsync())
            matches.Add(asyncRows.GetString(0));
        Assert.That(matches, Is.EqualTo(new[] { "near", "after", "last" }));
    }

    [Test]
    public async Task PartitionPruningFiltersAndPaginationPreserveSourceRowNumbers()
    {
        var first = Write("first.csv", "Id,Key\na,1\n# comment\nb,2\n");
        var selected = Write("selected.csv", "Id,Key\nlow,1\n# before\nother,2\n\"long\nquoted\",15\n"
            + "filtered,16\nlow-again,1\n# between\nother-again,2\nlast,17");
        var rejected = Write("rejected.csv", "Id,Key\nrejected,15\n");
        var query = Builder().WithDialect(d => d.WithCommentChar('#'))
            .WithParserOptimizations(new() { BufferSize = 7 })
            .WithRowNumber().WithSkip(1).WithTake(1)
            .FromFiles([first, selected, rejected]).WithPartitions(p => p.FromPath(path => path != rejected)).Build();
        var index = new ZoneMapIndex(new IndexKey<int>(1), 2);
        query.WithPartitionFilter(keep => keep).WithFilter("Id", "no matches").BuildIndexes(index);
        Assert.That(File.Exists(rejected + ".zonemap.idx"), Is.False);

        var lookup = query.WithIndexedLookup(index, new IndexRange<int>(15, 17))
            .WithPartitionFilter(keep => keep).WithFilter("Id", id => !id.SequenceEqual("filtered"));
        using (var sync = lookup.ToDataReader())
        {
            Assert.That(sync.Read(), Is.True);
            Assert.That(sync.GetString(1), Is.EqualTo("last"));
            Assert.That(sync.GetInt32(0), Is.EqualTo(9));
            Assert.That(sync.Read(), Is.False);
        }

        await using var asyncRows = lookup.ToDbDataReader();
        Assert.That(await asyncRows.ReadAsync(), Is.True);
        Assert.That(asyncRows.GetString(1), Is.EqualTo("last"));
        Assert.That(asyncRows.GetInt32(0), Is.EqualTo(9));
        Assert.That(await asyncRows.ReadAsync(), Is.False);
    }

    [Test]
    public void HeaderlessOrdinalLookupPreservesQuotedMultilineStringKeys()
    {
        var path = Write("headerless.csv", "1,Z\n2,z\n3,\"Aé\n\"\"B\"\"\"\n4,\"aé\n\"\"b\"\"\"\n");
        var csv = new CsvReaderBuilder().WithDialect(d => d.WithoutHeader().WithLineTerminator("\n")).Build();
        var index = new ZoneMapIndex(new IndexKey<string>(1, comparison: StringComparison.OrdinalIgnoreCase), 2);
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, "Aé\n\"B\"");
        Assert.That(Collect(rows), Is.EqualTo(new[] { "3", "4" }));
    }

    [TestCase("")]
    [TestCase("Id,Key\n")]
    [TestCase("Id,Key\none,NULL\ntwo")]
    public void EmptyAndUnboundedBlocksReload(string content)
    {
        var path = Write("empty.csv", content);
        var csv = Builder().WithDialect(d => d.WithHeader(content.Length != 0).WithNullSequence("NULL")).Build();
        var index = new ZoneMapIndex(new IndexKey<int>(1), 2);
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, new IndexRange<int>(0, 2));
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void MissingSidecarRequiresOptInAndOptionalLookupDoesNotCreateIt()
    {
        var path = Write("missing.csv", "Id,Key\none,1\ntwo,2\n");
        var csv = Builder().Build();
        var index = new ZoneMapIndex(new IndexKey<int>("Key"), 1).WithOptionalIndex();
        using (var strict = csv.WithIndexedLookup(path, index.WithRequiredIndex(), 2))
            Assert.Throws<FileNotFoundException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index, 2))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "two" }));
        Assert.That(File.Exists(path + ".zonemap.idx"), Is.False);
    }

    [TestCase("checksum")]
    [TestCase("version")]
    [TestCase("unreadable")]
    [TestCase("length")]
    [TestCase("timestamp")]
    [TestCase("block size")]
    [TestCase("key")]
    [TestCase("settings")]
    public void SupportedSourcesRejectInvalidSidecarsUnderEitherPolicy(string failure)
    {
        var path = Write("invalid.csv", "Id,Key\none,1\ntwo,2\n");
        var csv = Builder().Build();
        var index = new ZoneMapIndex(new IndexKey<int>("Key"), 1);
        csv.BuildIndexes(path, index);
        var sidecar = path + ".zonemap.idx";
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
                File.AppendAllText(path, "three,3\n");
                File.SetLastWriteTimeUtc(path, stamp);
                break;
            case "timestamp":
                File.SetLastWriteTimeUtc(path, stamp.AddHours(-1));
                break;
            case "block size":
                index = new ZoneMapIndex(index.Key, 2);
                break;
            case "key":
                index = new ZoneMapIndex(new IndexKey<int>(0), 1);
                break;
            case "settings":
                csv = Builder().WithDialect(d => d.WithNullSequence("2")).Build();
                break;
        }
        AssertRejected<InvalidDataException>();

        void AssertRejected<TException>()
            where TException : Exception
        {
            using var strict = csv.WithIndexedLookup(path, index, 2);
            Assert.Throws<TException>(() => strict.Read());
            using var optional = csv.WithIndexedLookup(path, index.WithOptionalIndex(), 2);
            Assert.Throws<TException>(() => optional.Read());
        }
    }

    [Test]
    public void Utf16BomRequiresOptionalScan()
    {
        var path = Path.Combine(_directory, "utf16.csv");
        File.WriteAllText(path, "Id,Key\none,1\ntwo,2\n", Encoding.Unicode);
        var csv = Builder().Build();
        var index = new ZoneMapIndex(new IndexKey<int>("Key"), 2);
        Assert.Throws<NotSupportedException>(() => csv.BuildIndexes(path, index));
        using (var strict = csv.WithIndexedLookup(path, index, 2))
            Assert.Throws<NotSupportedException>(() => strict.Read());
        using var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), new IndexRange<int>(2, 3));
        Assert.That(Collect(scan), Is.EqualTo(new[] { "two" }));
    }

    [Test]
    public void UnsupportedQueryConfigurationRequiresOptionalScan()
    {
        var path = Write("shared-header.csv", "Id,Key\none,1\ntwo,2\n");
        var query = Builder().WithDialect(d => d.WithHeaderRepeat(false)).FromFiles([path])
            .WithPartitions(p => p.FromPath(_ => true)).Build();
        var index = new ZoneMapIndex(new IndexKey<int>("Key"), 2);
        Assert.Throws<NotSupportedException>(() => query.BuildIndexes(index));
        Assert.Throws<NotSupportedException>(() => query.WithIndexedLookup(index, 2));
        using var scan = query.WithIndexedLookup(index.WithOptionalIndex(), 2).ToDataReader();
        Assert.That(Collect(scan), Is.EqualTo(new[] { "two" }));
    }

    [Test]
    public void RuntimeParserIsUsedToBuildAndFilterButExplicitFormatOverridesIt()
    {
        var path = Write("dates.csv", "Id,Key\nrecent,19/05/2026\nold,01/01/2025\nother,15/05/2026\n");
        var csv = Builder().WithParsers(p => p.WithParser<DateOnly>(static value =>
            DateOnly.ParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture))).Build();
        var index = new ZoneMapIndex(new IndexKey<DateOnly>("Key"), 1);
        var range = new IndexRange<DateOnly>(new(2026, 5, 15), new(2026, 5, 20));
        csv.BuildIndexes(path, index);
        using (var rows = csv.WithIndexedLookup(path, index, range))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "recent", "other" }));
        var plain = Builder().Build();
        using (var incompatible = plain.WithIndexedLookup(path, index.WithOptionalIndex(), range))
            Assert.Throws<InvalidDataException>(() => incompatible.Read());

        var explicitFormat = new ZoneMapIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"), 1);
        csv.BuildIndexes(path, explicitFormat);
        using var explicitRows = plain.WithIndexedLookup(path, explicitFormat, range);
        Assert.That(Collect(explicitRows), Is.EqualTo(new[] { "recent", "other" }));
    }

    [Test]
    public void OpenReaderRequiresOptionalLookupAndTransfersOwnership()
    {
        using var stream = new MemoryStream("Id,Key\none,01/01/2025\nmissing\ntwo,15/05/2026\n"u8.ToArray());
        var input = Builder().Build().ToDataReader(stream);
        var index = new ZoneMapIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"), 1);
        var range = new IndexRange<DateOnly>(new(2026, 5, 15), new(2026, 5, 16));
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index, range));
        using (var rows = input.WithIndexedLookup(index.WithOptionalIndex(), range))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "two" }));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void InvalidDefinitionsAndEndpointsFailBeforeOpeningAFile()
    {
        var csv = Builder().Build();
        var key = new IndexKey<int>("Key");
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneMapIndex(key, 0));
        var index = new ZoneMapIndex(key, 1);
        Assert.Throws<ArgumentException>(() => csv.WithIndexedLookup("missing.csv", index, "2"));
        Assert.Throws<ArgumentException>(() => csv.WithIndexedLookup("missing.csv", index, new IndexRange<int>(3, 2)));
        Assert.Throws<ArgumentNullException>(() => new IndexRange<string>(null!, "z"));
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, Encoding.UTF8);
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
