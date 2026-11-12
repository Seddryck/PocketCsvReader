using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class InvertedIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-inverted-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void AnyAndAllMatchTheOptionalScanWithRepeatedAndMissingTerms()
    {
        var path = Write("orders.csv", "Id,Tags\na,\"urgent, refund, URGENT\"\nb,refund\nc,\"return,urgent\"\nd,\n"
            + "e\nf,NULL\ng,\"refund,URGENT\"\nh,other\n");
        var builder = Builder().WithDialect(d => d.WithNullSequence("NULL"));
        var query = builder.FromDirectory(_directory, "*.csv").WithPartitions(p => p.HiveStyle()).Build();
        var index = TagsIndex();
        (TermPredicate Predicate, string[] Ids)[] cases =
        [
            (TermPredicate.ContainsAny("refund", "URGENT", "refund"), ["a", "b", "c", "g"]),
            (TermPredicate.ContainsAll("refund", "URGENT", "urgent"), ["a", "g"]),
            (TermPredicate.ContainsAll("urgent"), ["a", "c", "g"]),
            (TermPredicate.ContainsAny("return", "absent"), ["c"]),
            (TermPredicate.ContainsAll("urgent", "absent"), []),
            (TermPredicate.ContainsAny("NULL", "absent"), []),
            (TermPredicate.ContainsAny<string>(), []),
            (TermPredicate.ContainsAll<string>(), [])
        ];
        foreach (var (predicate, ids) in cases)
        {
            using var scan = query.WithIndexedLookup(index.WithOptionalIndex(), predicate).ToDataReader();
            Assert.That(Collect(scan), Is.EqualTo(ids));
        }
        Assert.That(File.Exists(path + ".inverted.idx"), Is.False);

        query.BuildIndexes(index);
        var reloaded = builder.FromDirectory(_directory, "*.csv").WithPartitions(p => p.HiveStyle()).Build();
        foreach (var (predicate, ids) in cases)
        {
            using var indexed = reloaded.WithIndexedLookup(TagsIndex(), predicate).ToDataReader();
            Assert.That(Collect(indexed), Is.EqualTo(ids));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadsOnlyMatchingPostingsAndSkipsAnAbsentRequiredTerm(bool emitBom)
    {
        const string data = "Id,Tags,Note\na,\"urgent,\nrefund\",\"first\nmultiline\"\nbadx,urgent,ok\n"
            + "c,refund,ok\nd,\"refund,URGENT\",\"last\nmultiline\"";
        var encoding = new UTF8Encoding(emitBom);
        var path = Write("selective.csv", data, encoding);
        var csv = Builder().WithParserOptimizations(new() { BufferSize = 5 }).Build();
        var index = TagsIndex();
        csv.BuildIndexes(path, index);

        // Preserve the source stamps so the malformed row reveals whether
        // lookup parsed a row outside the posting intersection.
        var stamp = File.GetLastWriteTimeUtc(path);
        Write("selective.csv", data.Replace("badx", "b,a,", StringComparison.Ordinal), encoding);
        File.SetLastWriteTimeUtc(path, stamp);
        using (var any = csv.WithIndexedLookup(path, index, TermPredicate.ContainsAny("urgent", "refund")))
            Assert.Throws<InvalidDataException>(() => Collect(any));

        var both = TermPredicate.ContainsAll("refund", "urgent");
        using (var indexed = csv.WithIndexedLookup(path, index, both))
            Assert.That(Collect(indexed), Is.EqualTo(new[] { "a", "d" }));
        await using (var indexed = csv.WithIndexedLookup(path, index, both))
        {
            var matches = new List<string>();
            while (await indexed.ReadAsync())
                matches.Add(indexed.GetString(0));
            Assert.That(matches, Is.EqualTo(new[] { "a", "d" }));
        }

        using var absent = csv.WithIndexedLookup(path, index, TermPredicate.ContainsAll("urgent", "absent"));
        Assert.That(absent.Read(), Is.False);
    }

    [Test]
    public void NormalizesBothRequestedAndExtractedTermsWithTheConfiguredComparison()
    {
        var path = Write("normalization.csv", "Id,Tags\na,\"CAFÉ, CAFÉ\"\nb,Cafe\nc,café\n");
        var csv = Builder().Build();
        var index = new InvertedIndex(new IndexKey<string>("Tags"), ExtractTags, NormalizeTag,
            termComparison: StringComparison.OrdinalIgnoreCase);
        var predicate = TermPredicate.ContainsAll(" café ", "CAFÉ");
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a", "c" }));
        csv.BuildIndexes(path, index);
        using var indexed = csv.WithIndexedLookup(path, index, predicate);
        Assert.That(Collect(indexed), Is.EqualTo(new[] { "a", "c" }));
    }

    [Test]
    public void HeaderlessTypedFieldUsesItsRuntimeParserBeforeExtraction()
    {
        var path = Write("dates.csv", "a,15/05/2026\nb,01/01/2025\nc,15/04/2026\nd,04/05/2025\n");
        var builder = Builder().WithDialect(d => d.WithoutHeader());
        var csv = builder.WithParsers(p => p.WithParser<DateOnly>(static text =>
            DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture))).Build();
        var index = InvertedIndex.Create(new IndexKey<DateOnly>(1), ExtractYearAndMonth);
        var predicate = TermPredicate.ContainsAll(2026, 5);
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));
        csv.BuildIndexes(path, index);
        using (var indexed = csv.WithIndexedLookup(path, index, predicate))
            Assert.That(Collect(indexed), Is.EqualTo(new[] { "a" }));

        var plain = Builder().WithDialect(d => d.WithoutHeader()).Build();
        using (var incompatible = plain.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.Throws<InvalidDataException>(() => incompatible.Read());
        var explicitFormat = InvertedIndex.Create(new IndexKey<DateOnly>(1, format: "dd/MM/yyyy"), ExtractYearAndMonth);
        csv.BuildIndexes(path, explicitFormat);
        using var explicitRows = plain.WithIndexedLookup(path, explicitFormat, predicate);
        Assert.That(Collect(explicitRows), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void PartitionFiltersRowFiltersAndPaginationPreserveSourceRowNumbers()
    {
        var first = Write("first.csv", "Id,Tags\na,other\n# comment\nb,refund\n");
        var selected = Write("selected.csv", "Id,Tags\na,other\n# before\nb,\"urgent,refund\"\nc,\"refund,urgent\"\n"
            + "d,refund\n# between\ne,\"refund,URGENT\"");
        var rejected = Write("rejected.csv", "Id,Tags\nrejected,\"urgent,refund\"\n");
        var query = Builder().WithDialect(d => d.WithCommentChar('#')).WithRowNumber().WithSkip(1).WithTake(1)
            .FromFiles([first, selected, rejected]).WithPartitions(p => p.FromPath(path => path != rejected)).Build();
        var index = TagsIndex();
        query.WithPartitionFilter(keep => keep).WithFilter("Id", "never").BuildIndexes(index);
        Assert.That(File.Exists(rejected + ".inverted.idx"), Is.False);

        var predicate = TermPredicate.ContainsAll("urgent", "refund");
        using var rows = query.WithIndexedLookup(index, predicate).WithPartitionFilter(keep => keep)
            .WithFilter("Id", id => !id.SequenceEqual("c")).ToDataReader();
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.GetString(1), Is.EqualTo("e"));
        Assert.That(rows.GetInt32(0), Is.EqualTo(7));
        Assert.That(rows.Read(), Is.False);
    }

    [TestCase("")]
    [TestCase("Id,Tags\n")]
    [TestCase("Id,Tags\na,NULL\nb\nc,")]
    public void EmptyPostingListsCanBeReloaded(string content)
    {
        var path = Write("empty.csv", content);
        var csv = Builder().WithDialect(d => d.WithHeader(content.Length != 0).WithNullSequence("NULL")).Build();
        var index = new InvertedIndex(new IndexKey<string>(1), ExtractTags);
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, TermPredicate.ContainsAny("urgent"));
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void MissingSidecarRequiresOptInAndOptionalLookupDoesNotCreateIt()
    {
        var path = Write("missing.csv", "Id,Tags\na,\"urgent,refund\"\nb,returned\n");
        var csv = Builder().Build();
        var index = TagsIndex().WithOptionalIndex();
        var predicate = TermPredicate.ContainsAny("urgent");
        using (var strict = csv.WithIndexedLookup(path, index.WithRequiredIndex(), predicate))
            Assert.Throws<FileNotFoundException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index, predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));
        Assert.That(File.Exists(path + ".inverted.idx"), Is.False);
    }

    [Test]
    public void SourceChangingDuringBuildDoesNotReplaceTheSidecar()
    {
        var path = Path.Combine(_directory, "changed.csv");
        Write("changed.csv", $"Id,Tags\na,\"{path}\"\n");
        var csv = Builder().WithDialect(d => d.WithEscapeChar(null)).Build();
        csv.BuildIndexes(path, TagsIndex(static value => [value]));
        var sidecar = path + ".inverted.idx";
        var original = File.ReadAllBytes(sidecar);

        Assert.Throws<IOException>(() => csv.BuildIndexes(path, TagsIndex(ChangeSourceStamp)));
        Assert.That(File.ReadAllBytes(sidecar), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [TestCase("checksum")]
    [TestCase("version")]
    [TestCase("unreadable")]
    [TestCase("length")]
    [TestCase("timestamp")]
    [TestCase("extractor")]
    [TestCase("normalizer")]
    [TestCase("comparison")]
    [TestCase("settings")]
    public void SupportedSourcesRejectInvalidSidecarsUnderEitherPolicy(string failure)
    {
        var path = Write("invalid.csv", "Id,Tags\na,\"urgent,refund\"\nb,returned\n");
        var csv = Builder().Build();
        var index = TagsIndex();
        var predicate = TermPredicate.ContainsAny("urgent");
        csv.BuildIndexes(path, index);
        var sidecar = path + ".inverted.idx";
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
                File.AppendAllText(path, "c,urgent\n");
                File.SetLastWriteTimeUtc(path, stamp);
                break;
            case "timestamp":
                File.SetLastWriteTimeUtc(path, stamp.AddHours(-1));
                break;
            case "extractor":
                index = TagsIndex(static value => value.Split('|'));
                break;
            case "normalizer":
                index = new InvertedIndex(TagsKey(), ExtractTags, NormalizeTag);
                break;
            case "comparison":
                index = new InvertedIndex(TagsKey(), ExtractTags, termComparison: StringComparison.Ordinal);
                break;
            case "settings":
                csv = Builder().WithDialect(d => d.WithNullSequence("returned")).Build();
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
        const string data = "Id,Tags\na,\"urgent,refund\"\nb,returned\n";
        var path = Write("utf16.csv", data, Encoding.Unicode);
        var csv = Builder().Build();
        var index = TagsIndex();
        var predicate = TermPredicate.ContainsAll("urgent", "refund");
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

    [TestCase(true)]
    [TestCase(false)]
    public void CapturingDelegatesCanScanButCannotBePersisted(bool capturingExtractor)
    {
        var path = Write("captured.csv", "Id,Tags\na,\"urgent,refund\"\nb,returned\n");
        var csv = Builder().Build();
        char delimiter = ',';
        char trim = ' ';
        var index = capturingExtractor
            ? TagsIndex(value => value.Split(delimiter))
            : new InvertedIndex(TagsKey(), ExtractTags, term => term.Trim(trim));
        var predicate = TermPredicate.ContainsAll("urgent", "refund");
        Assert.Throws<NotSupportedException>(() => csv.BuildIndexes(path, index));
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "a" }));
        Assert.That(File.Exists(path + ".inverted.idx"), Is.False);

        csv.BuildIndexes(path, TagsIndex());
        using var rejected = csv.WithIndexedLookup(path, index.WithOptionalIndex(), predicate);
        Assert.Throws<NotSupportedException>(() => rejected.Read());
    }

    [Test]
    public void OpenReaderRequiresOptionalLookupAndTransfersOwnership()
    {
        using var stream = new MemoryStream("Id,Date\na,01/01/2025\nb\nc,15/05/2026\n"u8.ToArray());
        var input = Builder().Build().ToDataReader(stream);
        var index = InvertedIndex.Create(new IndexKey<DateOnly>("Date", format: "dd/MM/yyyy"), ExtractYearAndMonth);
        var predicate = TermPredicate.ContainsAll(2026, 5);
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index, predicate));
        using (var rows = input.WithIndexedLookup(index.WithOptionalIndex(), predicate))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "c" }));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void TermTypesAndValuesAreValidatedBeforeOpeningFiles()
    {
        var csv = Builder().Build();
        var index = TagsIndex();
        Assert.Throws<ArgumentException>(() => TermPredicate.ContainsAny<string>("urgent", null!));
        Assert.Throws<ArgumentException>(() => csv.WithIndexedLookup("missing.csv", index, TermPredicate.ContainsAll(7)));
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private static IndexKey<string> TagsKey() => new("Tags", comparison: StringComparison.OrdinalIgnoreCase);

    private static InvertedIndex TagsIndex(Func<string, IEnumerable<string>>? extractor = null)
        => new(TagsKey(), extractor ?? ExtractTags);

    private static string[] ExtractTags(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int[] ExtractYearAndMonth(DateOnly value) => [value.Year, value.Month];

    private static string NormalizeTag(string term) => term.Trim().Normalize();

    private static string[] ChangeSourceStamp(string path)
    {
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddHours(-1));
        return [path];
    }

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
