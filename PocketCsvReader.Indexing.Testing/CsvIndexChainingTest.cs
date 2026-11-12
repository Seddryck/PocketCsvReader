using System.Data;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class CsvIndexChainingTest
{
    private static readonly DateOnly Date = new(2026, 5, 17);
    private static readonly IndexRange<decimal> Amounts = new(100m, 200m, includeUpper: false);
    private static readonly BitmapPredicate Categories = BitmapPredicate.In("Status", "shipped", "delivered")
        .And(BitmapPredicate.Equal("Region", "EU"));
    private static readonly TermPredicate Tags = TermPredicate.ContainsAll("urgent", "refund");
    private static readonly string Orders = """
        Id,Date,Customer,Amount,Status,Region,Tags,Warehouse,Keep
        before,2026-05-16,ACME,150,shipped,EU,"urgent,refund",17,yes
        a,2026-05-17,acme,100,shipped,EU,"URGENT,refund,urgent",17,yes
        customer,2026-05-17,other,150,shipped,EU,"urgent,refund",17,yes
        amount,2026-05-17,acme,200,shipped,EU,"urgent,refund",17,yes
        category,2026-05-17,acme,150,shipped,US,"urgent,refund",17,yes
        tag,2026-05-17,acme,150,shipped,EU,urgent,17,yes
        warehouse,2026-05-17,acme,150,shipped,EU,"urgent,refund",9,yes
        b,2026-05-17,ACME,175,delivered,EU,"refund,urgent",17,yes
        hidden,2026-05-17,acme,175,delivered,EU,"refund,urgent",17,no
        c,2026-05-17,Acme,199,shipped,EU,"refund,urgent",17,yes
        after,2026-05-18,ACME,150,shipped,EU,"urgent,refund",17,yes
        """.ReplaceLineEndings("\n");

    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-index-chaining-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void AllSixIndexesMatchSequentialFilteringInEveryCallOrder()
    {
        var query = Builder().WithRowNumber().FromFiles(new[] { FileSource.Partitioned(Write("orders.csv", Orders), true) }).Build();
        var indexes = new TestIndexes();
        indexes.Build(query);
        var expected = SequentialMatches(query);
        Assert.That(expected, Is.EqualTo(new[] { (2, "a"), (8, "b"), (10, "c") }));

        foreach (var order in Permutations(Enum.GetValues<CsvIndexKind>(), 0))
        {
            using var rows = indexes.Lookup(query, order).WithFilter("Keep", "yes").ToDataReader();
            var found = new List<(int RowNumber, string Id)>();
            while (rows.Read())
                found.Add((rows.GetInt32(0), rows.GetString(1)));
            Assert.That(found, Is.EqualTo(expected), string.Join(", ", order));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PartitionAndBloomPruneBeforeOpeningFilesAndKeepRowNumbers(bool bloomFirst)
    {
        var rejected = Path.Combine(_directory, "excluded.csv");
        var negative = Write("negative.csv", "Id,Date,Customer,Keep\nx,2026-05-17,other,yes\ny,2026-05-17,other,yes\n");
        var surviving = Write("surviving.csv", "Id,Date,Customer,Keep\na,2026-05-17,acme,yes\nb,2026-05-17,other,yes\n"
            + "# comment\nc,2026-05-17,acme,no\nd,2026-05-17,ACME,yes\n"
            + "e,2026-05-18,other,yes\nf,2026-05-19,other,yes\n");
        var last = Write("last.csv", "Id,Date,Customer,Keep\ng,2026-05-17,acme,yes\n");
        var csv = Builder().WithDialect(d => d.WithCommentChar('#')).Build();
        var indexes = new TestIndexes();
        csv.BuildIndexes(negative, indexes.Bloom);
        csv.BuildIndexes(surviving, indexes.Bloom);
        csv.BuildIndexes(surviving, indexes.Sparse);
        csv.BuildIndexes(last, indexes.Bloom);
        csv.BuildIndexes(last, indexes.Sparse);
        using var locked = File.Open(negative, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var query = Builder().WithDialect(d => d.WithCommentChar('#')).WithRowNumber().WithSkip(1).WithTake(2)
            .FromFiles(new[] { FileSource.Partitioned(rejected, false), FileSource.Partitioned(negative, true),
                FileSource.Partitioned(surviving, true), FileSource.Partitioned(last, true) }).Build();
        var order = bloomFirst ? new[] { CsvIndexKind.Bloom, CsvIndexKind.Sparse } : [CsvIndexKind.Sparse, CsvIndexKind.Bloom];
        var lookup = indexes.Lookup(query.WithFilter("Keep", "yes"), order).WithPartitionFilter(keep => keep);

        using (var sync = lookup.ToDataReader())
        {
            Assert.That(sync.Read(), Is.True);
            Assert.That(sync.GetInt32(0), Is.EqualTo(6));
            Assert.That(sync.GetString(1), Is.EqualTo("d"));
            Assert.That(sync.Read(), Is.True);
            Assert.That(sync.GetInt32(0), Is.EqualTo(9));
            Assert.That(sync.GetString(1), Is.EqualTo("g"));
            Assert.That(sync.Read(), Is.False);
        }
        await using var asyncRows = lookup.ToDbDataReader();
        Assert.That(await asyncRows.ReadAsync(), Is.True);
        Assert.That(asyncRows.GetInt32(0), Is.EqualTo(6));
        Assert.That(asyncRows.GetString(1), Is.EqualTo("d"));
        Assert.That(await asyncRows.ReadAsync(), Is.True);
        Assert.That(asyncRows.GetInt32(0), Is.EqualTo(9));
        Assert.That(asyncRows.GetString(1), Is.EqualTo("g"));
        Assert.That(await asyncRows.ReadAsync(), Is.False);
    }

    [Test]
    public async Task SparseCheckpointAndExactCandidatesSkipDisjointAndLaterRecords()
    {
        var data = string.Join('\n', Enumerable.Range(0, 15).Select(row =>
        {
            string id = row is 1 or 6 or 11 or 14 ? $"bad{row % 10}" : row.ToString(CultureInfo.InvariantCulture);
            var day = row < 4 ? "2026-05-16" : row < 13 ? "2026-05-17" : "2026-05-18";
            int amount = row is (>= 5 and <= 9) or 13 ? 900 : 150;
            return string.Create(CultureInfo.InvariantCulture,
                $"{id},{day},{amount},{(row == 11 ? 9 : 17)}");
        }));
        var source = "Id,Date,Amount,Warehouse\n" + data;
        var path = Write("checkpoint.csv", source);
        var query = Query(path);
        var sparse = new SparseIndex(new IndexKey<DateOnly>("Date"), 3);
        var zone = new ZoneMapIndex(new IndexKey<decimal>("Amount"), 5);
        var hash = new HashIndex(new IndexKey<int>("Warehouse"));
        query.BuildIndexes(sparse);
        query.BuildIndexes(zone);
        query.BuildIndexes(hash);

        // Row 1 precedes the checkpoint, row 6 is outside the zone map,
        // row 11 is outside the hash result, and row 14 follows the sparse stop
        // at row 13, which passes the zone bounds but fails its exact predicate.
        CorruptRows(path, source.Replace("bad1,", "b,a,,", StringComparison.Ordinal)
            .Replace("bad6,", "b,a,,", StringComparison.Ordinal).Replace("bad4,", "b,a,,", StringComparison.Ordinal));
        var lookup = query.WithIndexedLookup(sparse, Date).WithIndexedLookup(hash, 17).WithIndexedLookup(zone, Amounts);
        Assert.That(Collect(lookup), Is.EqualTo(new[] { "4", "10", "12" }));
        await using var rows = lookup.ToDbDataReader();
        var found = new List<string>();
        while (await rows.ReadAsync())
            found.Add(rows.GetString(0));
        Assert.That(found, Is.EqualTo(new[] { "4", "10", "12" }));
    }

    [TestCase("zone")]
    [TestCase("bitmap")]
    [TestCase("inverted")]
    [TestCase("hash")]
    public void EmptyCandidateStageSkipsLaterRequiredSidecarAndCsvRecords(string stage)
    {
        const string source = "Id,Date,Amount,Status,Region,Tags,Warehouse\nbadx,2026-05-17,900,returned,US,other,9\n";
        var path = Write("empty.csv", source);
        var query = Query(path);
        var indexes = new TestIndexes();
        var prune = BuildEmptyStage(query, indexes, stage);
        CorruptRows(path, source.Replace("badx,", "b,a,,", StringComparison.Ordinal));
        CsvIndexKind[] seekOrder = [CsvIndexKind.ZoneMap, CsvIndexKind.Bitmap, CsvIndexKind.Inverted, CsvIndexKind.Hash, CsvIndexKind.Sparse];
        var lookup = indexes.Lookup(query, seekOrder.SkipWhile(kind => kind != prune).Reverse());
        Assert.That(Collect(lookup), Is.Empty);
    }

    [Test]
    public void NonemptyZoneAndBitmapCanIntersectToEmptyBeforeHashLookup()
    {
        var path = Write("disjoint.csv", "Id,Amount,Status,Region\na,150,returned,EU\nb,900,shipped,EU\n");
        var query = Query(path);
        var zone = new ZoneMapIndex(new IndexKey<decimal>("Amount"), 1);
        var bitmap = new TestIndexes().Bitmap;
        query.BuildIndexes(zone);
        query.BuildIndexes(bitmap);
        Assert.That(Collect(query.WithIndexedLookup(zone, Amounts)), Is.EqualTo(new[] { "a" }));
        Assert.That(Collect(query.WithIndexedLookup(bitmap, Categories)), Is.EqualTo(new[] { "b" }));
        var lookup = query.WithIndexedLookup(new HashIndex(new IndexKey<int>("Missing")), 17)
            .WithIndexedLookup(bitmap, Categories).WithIndexedLookup(zone, Amounts);
        Assert.That(Collect(lookup), Is.Empty);
    }

    [TestCase(".bloom.idx")]
    [TestCase(".zonemap.idx")]
    [TestCase(".bitmap.idx")]
    [TestCase(".inverted.idx")]
    [TestCase(".hash.idx")]
    [TestCase(".sparse.idx")]
    public void ConsultedMissingSidecarFallsBackOnlyWhenOptionalAndInvalidSidecarsFail(string suffix)
    {
        var path = Write("missing.csv", Orders);
        var query = Query(path);
        var indexes = new TestIndexes();
        indexes.Build(query);
        var sidecar = path + suffix;
        File.Delete(sidecar);
        var order = Enum.GetValues<CsvIndexKind>().Reverse();
        var strict = indexes.Lookup(query, order);
        Assert.Throws<FileNotFoundException>(() => Collect(strict));

        var optional = indexes.Lookup(query, order, suffix).WithFilter("Keep", "yes");
        Assert.That(Collect(optional), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(File.Exists(sidecar), Is.False);

        File.WriteAllText(sidecar, "invalid");
        Assert.Throws<InvalidDataException>(() => Collect(optional));
    }

    [Test]
    public void OptionalIndexesCombineOnCustomSourcesWithoutConsultingSidecars()
    {
        var path = Path.Combine(_directory, "virtual.csv");
        var source = Encoding.UTF8.GetBytes(Orders);
        var query = Builder().FromFiles([path], _ => new MemoryStream(source))
            .WithPartitions(p => p.FromPath(_ => true)).Build();
        var optional = new TestIndexes().Lookup(query, Enum.GetValues<CsvIndexKind>(), "*").WithFilter("Keep", "yes");
        Assert.That(Collect(optional), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void OptionalSparseFallbackKeepsLaterMatchesInUnsortedHashCandidates()
    {
        var path = Write("unsorted.csv", "Id,Date,Warehouse\na,2026-05-18,17\nb,2026-05-17,17\nc,2026-05-17,9\n");
        var query = Query(path);
        var indexes = new TestIndexes();
        query.BuildIndexes(indexes.Hash);
        var lookup = query.WithIndexedLookup(indexes.Sparse.WithOptionalIndex(), Date).WithIndexedLookup(indexes.Hash, 17);
        Assert.That(Collect(lookup), Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public void BloomFalsePositiveStillChecksExactEqualityInSparseCandidates()
    {
        var data = string.Concat(Enumerable.Range(0, 1024).Select(i => string.Create(CultureInfo.InvariantCulture, $"{i},1\n")));
        var path = Write("saturated.csv", "Customer,Day\n" + data);
        var query = Query(path);
        var bloom = new BloomIndex(new IndexKey<int>("Customer"), maxBytes: 32);
        var sparse = new SparseIndex(new IndexKey<int>("Day"), 17);
        query.BuildIndexes(bloom);
        query.BuildIndexes(sparse);
        Assert.That(File.ReadAllBytes(path + ".bloom.idx").Skip(64).Take(32), Is.All.EqualTo(byte.MaxValue));
        Assert.That(Collect(query.WithIndexedLookup(sparse, 1).WithIndexedLookup(bloom, -1)), Is.Empty);
    }

    private static CsvIndexKind BuildEmptyStage(CsvFileQuery<bool> query, TestIndexes indexes, string stage)
    {
        switch (stage)
        {
            case "zone":
                query.BuildIndexes(indexes.ZoneMap);
                return CsvIndexKind.ZoneMap;
            case "bitmap":
                query.BuildIndexes(indexes.Bitmap);
                return CsvIndexKind.Bitmap;
            case "inverted":
                query.BuildIndexes(indexes.Inverted);
                return CsvIndexKind.Inverted;
            default:
                query.BuildIndexes(indexes.Hash);
                return CsvIndexKind.Hash;
        }
    }

    private static List<(int RowNumber, string Id)> SequentialMatches(CsvFileQuery<bool> query)
    {
        using var rows = query.ToDataReader();
        var found = new List<(int RowNumber, string Id)>();
        while (rows.Read())
        {
            var tags = ExtractTags(rows.GetString(7));
            if (rows.GetString(2) == "2026-05-17" && rows.GetString(3).Equals("acme", StringComparison.OrdinalIgnoreCase)
                && rows.GetDecimal(4) is >= 100m and < 200m && rows.GetString(5) is "shipped" or "delivered"
                && rows.GetString(6) == "EU" && tags.Contains("urgent", StringComparer.OrdinalIgnoreCase)
                && tags.Contains("refund", StringComparer.OrdinalIgnoreCase) && rows.GetInt32(8) == 17 && rows.GetString(9) == "yes")
                found.Add((rows.GetInt32(0), rows.GetString(1)));
        }
        return found;
    }

    private static IEnumerable<CsvIndexKind[]> Permutations(CsvIndexKind[] order, int start)
    {
        if (start == order.Length)
        {
            yield return (CsvIndexKind[])order.Clone();
            yield break;
        }
        for (int i = start; i < order.Length; i++)
        {
            (order[start], order[i]) = (order[i], order[start]);
            foreach (var permutation in Permutations(order, start + 1))
                yield return permutation;
            (order[start], order[i]) = (order[i], order[start]);
        }
    }

    private static void CorruptRows(string path, string replacement)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, replacement, Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, stamp);
    }

    private static string[] ExtractTags(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private static CsvFileQuery<bool> Query(string path)
        => Builder().FromFiles(new[] { FileSource.Partitioned(path, true) }).Build();

    private string Write(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents, Encoding.UTF8);
        return path;
    }

    private static List<string> Collect(CsvFileQuery<bool> query)
    {
        using IDataReader rows = query.ToDataReader();
        var result = new List<string>();
        while (rows.Read())
            result.Add(rows.GetString(0));
        return result;
    }

    private sealed class TestIndexes
    {
        internal BloomIndex Bloom { get; } = new(new IndexKey<string>("Customer", comparison: StringComparison.OrdinalIgnoreCase));
        internal SparseIndex Sparse { get; } = new(new IndexKey<DateOnly>("Date"), 3);
        internal ZoneMapIndex ZoneMap { get; } = new(new IndexKey<decimal>("Amount"), 2);
        internal BitmapIndex Bitmap { get; } = new(new IndexKey<string>("Status"), new IndexKey<string>("Region"));
        internal InvertedIndex Inverted { get; } = new(new IndexKey<string>("Tags", comparison: StringComparison.OrdinalIgnoreCase), ExtractTags);
        internal HashIndex Hash { get; } = new(new IndexKey<int>("Warehouse"));

        internal void Build(CsvFileQuery<bool> query)
        {
            query.BuildIndexes(Bloom);
            query.BuildIndexes(Sparse);
            query.BuildIndexes(ZoneMap);
            query.BuildIndexes(Bitmap);
            query.BuildIndexes(Inverted);
            query.BuildIndexes(Hash);
        }

        internal CsvFileQuery<bool> Lookup(CsvFileQuery<bool> query, IEnumerable<CsvIndexKind> order, string? optional = null)
        {
            foreach (var kind in order)
            {
                query = kind switch
                {
                    CsvIndexKind.Bloom => query.WithIndexedLookup(IsOptional(".bloom.idx") ? Bloom.WithOptionalIndex() : Bloom, "Acme"),
                    CsvIndexKind.ZoneMap => query.WithIndexedLookup(IsOptional(".zonemap.idx") ? ZoneMap.WithOptionalIndex() : ZoneMap, Amounts),
                    CsvIndexKind.Bitmap => query.WithIndexedLookup(IsOptional(".bitmap.idx") ? Bitmap.WithOptionalIndex() : Bitmap, Categories),
                    CsvIndexKind.Inverted => query.WithIndexedLookup(IsOptional(".inverted.idx") ? Inverted.WithOptionalIndex() : Inverted, Tags),
                    CsvIndexKind.Hash => query.WithIndexedLookup(IsOptional(".hash.idx") ? Hash.WithOptionalIndex() : Hash, 17),
                    CsvIndexKind.Sparse => query.WithIndexedLookup(IsOptional(".sparse.idx") ? Sparse.WithOptionalIndex() : Sparse, Date),
                    _ => throw new ArgumentOutOfRangeException(nameof(order))
                };
            }
            return query;

            bool IsOptional(string suffix) => optional == suffix || optional == "*";
        }
    }
}
