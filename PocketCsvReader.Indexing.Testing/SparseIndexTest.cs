using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class SparseIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-sparse-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task DateLookupMatchesSequentialFilterAcrossCheckpointsWithBomAndMultilineRecords()
    {
        var path = Write("orders.csv",
            "Date;Description\n2026-01-01;début\n2026-05-17;\"first\npart\"\n2026-05-17;second\n"
            + "2026-05-17;third\n2026-05-17;fourth\n2026-12-31;last", bom: true);
        var query = Query(path, new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n")));
        var index = new SparseIndex(new IndexKey<DateOnly>("Date", format: "yyyy-MM-dd"), 2);

        query.BuildIndexes(index);
        var lines = File.ReadAllLines(path + ".sparse.idx");
        Assert.That(lines[0], Is.EqualTo("format,pocket-sparse-index,1"));
        Assert.That(lines, Does.Contain("metadata,columnName,Date"));
        Assert.That(lines, Does.Contain("checkpoint,20,2026-01-01"));

        var date = new DateOnly(2026, 5, 17);
        var expected = new List<string>();
        using (var sequential = query.ToDataReader())
        {
            while (sequential.Read())
            {
                if (DateOnly.ParseExact(sequential.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture) == date)
                    expected.Add(sequential.GetString(1));
            }
        }

        var actual = new List<string>();
        await using var indexed = query.WithIndexedLookup(index, date).ToDataReader();
        while (await indexed.ReadAsync())
            actual.Add(indexed.GetString(1));
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual, Is.EqualTo(new[] { "first\npart", "second", "third", "fourth" }));
        Assert.That(Collect(query.WithIndexedLookup(index, new DateOnly(2026, 5, 18))), Is.Empty);
        Assert.That(Collect(query.WithIndexedLookup(index, new DateOnly(2026, 12, 31)), 1), Is.EqualTo(new[] { "last" }));
    }

    [Test]
    public void PartitionedCsvReusesTheRegisteredDateParserAndRejectsIncompatibleParsers()
    {
        var path = Write("orders.csv", "Date,Id\n31/01/2026,a\n17/05/2026,b\n17/05/2026,c\n01/12/2026,d\n");
        var index = new SparseIndex(new IndexKey<DateOnly>("Date"), 1);
        var date = new DateOnly(2026, 5, 17);
        ParseSpan<DateOnly> parseDate = ParseOrderDate;
        var builder = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));
        Query(path, builder.WithParsers(p => p.WithParser(parseDate))).BuildIndexes(index);

        var lookup = Query(path, new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithParsers(p => p.WithParser(parseDate)));
        Assert.That(Collect(lookup.WithIndexedLookup(index, date), 1), Is.EqualTo(new[] { "b", "c" }));
        AssertReadFails<InvalidDataException>(Query(path).WithIndexedLookup(index, date));

        var incompatible = Query(path, builder.WithParsers(p => p.WithParser<DateOnly>(
            static (ReadOnlySpan<char> _) => throw new AssertionException("An incompatible parser must not run."))));
        AssertReadFails<InvalidDataException>(incompatible.WithIndexedLookup(index.WithOptionalIndex(), date));

        static DateOnly ParseOrderDate(ReadOnlySpan<char> text)
            => DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    [Test]
    public void ExplicitIndexFormatTakesPrecedenceOverTheRegisteredParser()
    {
        var path = Write("dates.csv", "Date\n17/05/2026\n");
        var builder = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithParsers(p => p.WithParser<DateOnly>(
                static (ReadOnlySpan<char> _) => throw new AssertionException("The explicit index format must win.")));
        var query = Query(path, builder);
        var index = new SparseIndex(new IndexKey<DateOnly>("Date", format: "dd/MM/yyyy"), 1);
        query.BuildIndexes(index);

        Assert.That(Collect(query.WithIndexedLookup(index, new DateOnly(2026, 5, 17))), Is.EqualTo(new[] { "17/05/2026" }));
    }

    [Test]
    public void StringsRoundTripCsvEscapingAndUseConfiguredEquality()
    {
        var path = Write("strings.csv", "Name,Id\n\"aa,\"\"x\"\"\ny\",1\n\"AA,\"\"X\"\"\nY\",2\nz,3\n");
        var query = Query(path);
        var index = new SparseIndex(new IndexKey<string>("Name", comparison: StringComparison.OrdinalIgnoreCase), 1);
        query.BuildIndexes(index);
        Assert.That(File.ReadAllText(path + ".sparse.idx"), Does.Contain("\"aa,\"\"x\"\"\ny\""));

        var lookup = new SparseIndex(new IndexKey<string>("Name", comparison: StringComparison.OrdinalIgnoreCase), 5);
        Assert.That(Collect(query.WithIndexedLookup(lookup, "Aa,\"x\"\ny"), 1), Is.EqualTo(new[] { "1", "2" }));
        AssertReadFails<InvalidDataException>(query.WithIndexedLookup(
            new SparseIndex(new IndexKey<string>("Name", comparison: StringComparison.Ordinal), 1), "z"));
    }

    [Test]
    public void OrdinalLookupSeeksNearTheEndAndStopsAtTheFirstGreaterKey()
    {
        var path = Write("numbers.csv", string.Concat(Enumerable.Range(0, 2000).Select(i => $"{i:D4},{i}\n")));
        var query = Query(path, new CsvReaderBuilder().WithDialect(d => d.WithoutHeader().WithLineTerminator("\n")));
        var index = new SparseIndex(new IndexKey<int>(0), 32);
        query.BuildIndexes(index);
        Assert.That(File.ReadAllLines(path + ".sparse.idx"), Does.Contain("metadata,columnOrdinal,0"));

        // Keep the file stamp unchanged and place invalid keys outside the candidate region.
        // Parsing either poison record would prove the reader scanned beyond the region.
        var stamp = File.GetLastWriteTimeUtc(path);
        var source = File.ReadAllText(path);
        File.WriteAllText(path, source.Replace("0032,32\n", "xxxx,32\n").Replace("1999,1999\n", "xxxx,1999\n"));
        File.SetLastWriteTimeUtc(path, stamp);

        Assert.That(Collect(query.WithIndexedLookup(index, 1900), 1), Is.EqualTo(new[] { "1900" }));
        Assert.That(Collect(query.WithIndexedLookup(index, -1)), Is.Empty);
    }

    [Test]
    public void PartitionPruningAndRowNumberingWorkAcrossFiles()
    {
        var a = Write("a.csv", "Key,Id\n1,a\n2,b\n2,c\n3,d\n4,e\n");
        var b = Write("b.csv", "Key,Id\n0,f\n1,g\n2,h\n3,i\n");
        var missing = Path.Combine(_directory, "missing.csv");
        var builder = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithRowNumber().WithFilter("Id", value => value.Length == 1).WithSkip(1).WithTake(2);
        var files = new[] { FileSource.Partitioned(a, true), FileSource.Partitioned(missing, false), FileSource.Partitioned(b, true) };
        var query = builder.FromFiles(files).Build();
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        query.WithPartitionFilter(keep => keep).BuildIndexes(index);

        using var reader = query.WithIndexedLookup(index, 2).WithPartitionFilter(keep => keep).ToDataReader();
        var numbers = new List<int>();
        while (reader.Read())
            numbers.Add(reader.GetInt32(0));
        Assert.That(numbers, Is.EqualTo(new[] { 3, 8 }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void QueryRowFilterWorksBeforeOrAfterLookup(bool useSidecar)
    {
        var orders = Write("orders-BE.csv",
            "Date,Status,OrderId,Channel\n" +
            "2026-05-16,Shipped,older,Web\n" +
            "2026-05-17,Pending,pending,Web\n" +
            "2026-05-17,shipped,lowercase,Web\n" +
            "2026-05-17,Shipped,store,Store\n" +
            "2026-05-17,Shipped,first,Web\n" +
            "2026-05-17,Shipped,second,Web\n" +
            "2026-05-18,Shipped,later,Web\n");
        var excluded = Path.Combine(_directory, "orders-FR.csv");
        var files = new[] { FileSource.Partitioned(orders, true), FileSource.Partitioned(excluded, false) };
        var selected = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithFilter("Channel", "Web")
            .WithSkip(1).WithTake(1)
            .FromFiles(files).Build().WithPartitionFilter(keep => keep);
        var index = new SparseIndex(new IndexKey<DateOnly>("Date"), 1);
        if (useSidecar)
            selected.WithFilter("Status", "Unmatched").BuildIndexes(index);
        else
            index = index.WithOptionalIndex();

        var date = new DateOnly(2026, 5, 17);
        var before = selected.WithFilter("Status", "Shipped").WithIndexedLookup(index, date);
        var lookup = selected.WithIndexedLookup(index, date);
        var after = lookup.WithFilter("Status", "Shipped");

        Assert.That(Collect(before, 2), Is.EqualTo(new[] { "second" }));
        Assert.That(Collect(after, 2), Is.EqualTo(new[] { "second" }));
        Assert.That(Collect(lookup, 2), Is.EqualTo(new[] { "lowercase" }));
    }

    [Test]
    public void AQueryCombinesIndexedFilesAndUnsortedScanFallbacks()
    {
        var sorted = Write("sorted.csv", "Key,Id\n1,a\n2,b\n3,c\n");
        var unsorted = Write("unsorted.csv", "Key,Id\n3,d\n2,e\n1,f\n2,g\n");
        var descriptors = new[] { FileSource.Partitioned(sorted, true), FileSource.Partitioned(unsorted, false) };
        var query = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .FromFiles(descriptors).Build();
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        query.WithPartitionFilter(selected => selected).BuildIndexes(index);

        Assert.That(Collect(query.WithIndexedLookup(index.WithOptionalIndex(), 2), 1), Is.EqualTo(new[] { "b", "e", "g" }));
        Assert.That(File.Exists(unsorted + ".sparse.idx"), Is.False);
    }

    [Test]
    public void ACustomCsvSourceUsesTheScanFallbackBeforeTheQueryWindow()
    {
        var files = new[] { FileSource.Partitioned("virtual.csv", true,
            () => new MemoryStream("Key,Id\n3,a\n2,b\n1,c\n2,d\n"u8.ToArray())) };
        var query = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSkip(1).WithTake(1).FromFiles(files).Build();
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);

        Assert.That(Collect(query.WithIndexedLookup(index.WithOptionalIndex(), 2), 1), Is.EqualTo(new[] { "d" }));
        AssertReadFails<NotSupportedException>(query.WithIndexedLookup(index, 2));
    }

    [Test]
    public void IncompatibleColumnParserAndSourceDialectAreRejected()
    {
        var path = Write("dates.csv", "Date,Other\n2026-01-01,2026-01-01\n2026-05-17,2026-05-17\n");
        var query = Query(path);
        var index = new SparseIndex(new IndexKey<DateOnly>("Date"), 1);
        query.BuildIndexes(index);
        var key = new DateOnly(2026, 5, 17);
        Assert.That(Collect(query.WithIndexedLookup(
            new SparseIndex(new IndexKey<DateOnly>("Date", format: "yyyy-MM-dd", culture: CultureInfo.InvariantCulture), 1), key)),
            Is.EqualTo(new[] { "2026-05-17" }));
        AssertReadFails<InvalidDataException>(query.WithIndexedLookup(new SparseIndex(new IndexKey<DateOnly>("Other"), 1), key));
        AssertReadFails<InvalidDataException>(query.WithIndexedLookup(
            new SparseIndex(new IndexKey<DateOnly>("Date", format: "dd/MM/yyyy"), 1), key));
        AssertReadFails<InvalidDataException>(Query(path, new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n"))).WithIndexedLookup(index, key));
    }

    [TestCase("missing")]
    [TestCase("corrupt")]
    [TestCase("unreadable")]
    [TestCase("length")]
    [TestCase("timestamp")]
    public void LookupFailsForInvalidSidecars(string failure)
    {
        var path = Write("numbers.csv", "Key\n1\n2\n");
        var query = Query(path);
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        query.BuildIndexes(index);
        var sidecar = path + ".sparse.idx";
        var stamp = File.GetLastWriteTimeUtc(path);
        switch (failure)
        {
            case "missing":
                File.Delete(sidecar);
                AssertReadFails<FileNotFoundException>(query.WithIndexedLookup(index, 2));
                return;
            case "corrupt":
                File.WriteAllText(sidecar, File.ReadAllText(sidecar).Replace("checkpoint,6,2", "checkpoint,5,2"));
                break;
            case "unreadable":
                using (var locked = new FileStream(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    AssertReadFails<IOException>(query.WithIndexedLookup(index, 2));
                return;
            case "length":
                File.AppendAllText(path, "3\n");
                File.SetLastWriteTimeUtc(path, stamp);
                break;
            case "timestamp":
                File.SetLastWriteTimeUtc(path, stamp.AddHours(-1));
                break;
        }
        AssertReadFails<InvalidDataException>(query.WithIndexedLookup(index, 2));
    }

    [Test]
    public void WrongKeyTypeFailsBeforeFileDiscovery()
    {
        var discovered = false;
        IEnumerable<PartitionedFile<bool>> Files()
        {
            discovered = true;
            yield return FileSource.Partitioned("missing.csv", true);
        }

        var query = new CsvReaderBuilder().FromFiles(Files()).Build();
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        Assert.Throws<ArgumentException>(() => query.WithIndexedLookup(index, "2"));
        Assert.That(discovered, Is.False);
    }

    [TestCase("MM/dd")]
    [TestCase("MM/dd/'yyyy'")]
    public void DateFormatMustIncludeTheYear(string format)
        => Assert.Throws<ArgumentException>(() => new IndexKey<DateOnly>("Date", format));

    [Test]
    public void UnsortedInputDoesNotPublishASidecar()
    {
        var path = Write("numbers.csv", "Key\n2\n1\n");
        var index = new SparseIndex(new IndexKey<int>("Key"), 1);
        Assert.Throws<InvalidDataException>(() => Query(path).BuildIndexes(index));
        Assert.That(File.Exists(path + ".sparse.idx"), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void SourceChangingDuringTheScanDoesNotPublishASidecar(bool lengthChanges, bool bloom)
    {
        var path = Write("numbers.csv", "Key\n1\n2\n");
        var stamp = File.GetLastWriteTimeUtc(path);
        var key = new MutatingKey(() =>
        {
            if (lengthChanges)
            {
                using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                writer.Seek(0, SeekOrigin.End);
                writer.Write("3\n"u8);
            }
            File.SetLastWriteTimeUtc(path, lengthChanges ? stamp : stamp.AddHours(-1));
        });
        var query = Query(path);
        if (bloom)
        {
            var valid = new BloomIndex(new IndexKey<int>("Key"));
            query.BuildIndexes(valid);
            var sidecar = path + ".bloom.idx";
            var original = File.ReadAllBytes(sidecar);
            Assert.Throws<IOException>(() => query.BuildIndexes(new BloomIndex(key)));
            Assert.That(File.ReadAllBytes(sidecar), Is.EqualTo(original));
        }
        else
        {
            Assert.Throws<IOException>(() => query.BuildIndexes(new SparseIndex(key, 1)));
            Assert.That(File.Exists(path + ".sparse.idx"), Is.False);
        }
    }

    [TestCase("zonemap")]
    [TestCase("hash")]
    [TestCase("bitmap")]
    public void SourceChangingDuringTheScanDoesNotReplaceTheSeekIndex(string indexKind)
    {
        var path = Write("numbers.csv", "Key\n1\n2\n");
        var stamp = File.GetLastWriteTimeUtc(path);
        var query = Query(path);
        var key = new IndexKey<int>("Key");
        var sidecar = $"{path}.{indexKind}.idx";
        Build(key);
        var original = File.ReadAllBytes(sidecar);
        var mutating = new MutatingKey(() => File.SetLastWriteTimeUtc(path, stamp.AddHours(-1)));
        Assert.Throws<IOException>(() => Build(mutating));
        Assert.That(File.ReadAllBytes(sidecar), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"), Is.Empty);

        void Build(IIndexKey indexKey)
        {
            switch (indexKind)
            {
                case "hash":
                    query.BuildIndexes(new HashIndex(indexKey));
                    break;
                case "bitmap":
                    query.BuildIndexes(new BitmapIndex(indexKey));
                    break;
                default:
                    query.BuildIndexes(new ZoneMapIndex(indexKey, 1));
                    break;
            }
        }
    }

    private sealed class MutatingKey(Action mutate) : IIndexKey
    {
        private readonly IIndexKey _inner = new IndexKey<int>("Key");
        private bool _mutated;
        public Type KeyType => _inner.KeyType;
        string? IIndexKey.ColumnName => _inner.ColumnName;
        int? IIndexKey.ColumnOrdinal => _inner.ColumnOrdinal;
        string IIndexKey.CompatibilityId => _inner.CompatibilityId;
        int IIndexKey.Compare(object left, object right) => _inner.Compare(left, right);
        ulong IIndexKey.GetStableHash(object value) => _inner.GetStableHash(value);

        object IIndexKey.Parse(ReadOnlySpan<char> text)
        {
            if (!_mutated)
            {
                _mutated = true;
                mutate();
            }
            return _inner.Parse(text);
        }
    }

    private string Write(string name, string contents, bool bom = false)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents, new UTF8Encoding(bom));
        return path;
    }

    private static CsvFileQuery<bool> Query(string path, CsvReaderBuilder? builder = null)
        => (builder ?? new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n")))
            .FromFiles(new[] { FileSource.Partitioned(path, true) }).Build();

    private static string[] Collect(CsvFileQuery<bool> query, int column = 0)
    {
        using var reader = query.ToDataReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(reader.GetString(column));
        return [.. rows];
    }

    private static void AssertReadFails<T>(CsvFileQuery<bool> query)
        where T : Exception
    {
        using var reader = query.ToDataReader();
        Assert.Catch<T>(() => reader.Read());
    }
}
