using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class HashIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-hash-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(StringComparison.Ordinal, "z", "Z", "z")]
    [TestCase(StringComparison.OrdinalIgnoreCase, "äpfel", "ÄPFEL", "Äpfel")]
    [TestCase(StringComparison.InvariantCultureIgnoreCase, "café", "CAFÉ", "Café")]
    public void EqualityMatchesSequentialFilteringForUnsortedKeysAndDuplicates(
        StringComparison comparison, string first, string second, string requested)
    {
        var path = Write("orders.csv", $"Id,Key\n1,{first}\n2,a\n3,NULL\n4,{second}\n5\n6,{first}\n");
        var query = Builder().WithDialect(d => d.WithNullSequence("NULL"))
            .FromDirectory(_directory, "*.csv").WithPartitions(p => p.HiveStyle()).Build();
        var index = new HashIndex(new IndexKey<string>(1, comparison: comparison));
        var expected = new List<string>();
        using (var source = query.ToDataReader())
        {
            while (source.Read())
            {
                if (source.FieldCount > 1 && !source.IsDBNull(1) && string.Equals(source.GetString(1), requested, comparison))
                    expected.Add(source.GetString(0));
            }
        }

        using (var scan = query.WithIndexedLookup(index.WithOptionalIndex(), requested).ToDataReader())
            Assert.That(Collect(scan), Is.EqualTo(expected));
        Assert.That(File.Exists(path + ".hash.idx"), Is.False);

        query.BuildIndexes(index);
        using (var indexed = query.WithIndexedLookup(index, requested).ToDataReader())
            Assert.That(Collect(indexed), Is.EqualTo(expected));
        using (var absent = query.WithIndexedLookup(index, "absent").ToDataReader())
            Assert.That(absent.Read(), Is.False);
        using var nullKey = query.WithIndexedLookup(index, "NULL").ToDataReader();
        Assert.That(nullKey.Read(), Is.False);
    }

    [Test]
    public void ExactEqualityResolvesForcedHashCollisions()
    {
        var path = Write("collisions.csv", "Id,Key\n1,Bob\n2,Alice\n3,BOB\n4,Eve\n");
        var csv = Builder().Build();
        var key = new IndexKey<string>("Key", comparison: StringComparison.OrdinalIgnoreCase);
        var collision = new HashIndex(new ConstantHashKey(key));
        csv.BuildIndexes(path, collision);
        using (var matches = csv.WithIndexedLookup(path, collision, "bob"))
            Assert.That(Collect(matches), Is.EqualTo(new[] { "1", "3" }));
        using (var absent = csv.WithIndexedLookup(path, collision, "charlie"))
            Assert.That(absent.Read(), Is.False);

        using var incompatible = csv.WithIndexedLookup(path, new HashIndex(key).WithOptionalIndex(), "bob");
        Assert.Throws<InvalidDataException>(() => incompatible.Read());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SeeksBetweenQuotedMultilineRecordsWithoutParsingUnrelatedRecords(bool emitBom)
    {
        const string data = "Id,Key\n1,\"Aé\n\"\"B\"\"\"\nbadx,0\n3,other\n4,\"aé\n\"\"b\"\"\"";
        var path = Write("direct.csv", data, new UTF8Encoding(emitBom));
        var csv = Builder().WithParserOptimizations(new() { BufferSize = 5 }).Build();
        var index = new HashIndex(new IndexKey<string>("Key", comparison: StringComparison.OrdinalIgnoreCase));
        csv.BuildIndexes(path, index);

        // The same-length replacement makes a sequential parse fail. Restore
        // the timestamp so the test can observe which records were parsed.
        var stamp = File.GetLastWriteTimeUtc(path);
        Write("direct.csv", data.Replace("badx,0", "b,a,d,", StringComparison.Ordinal), new UTF8Encoding(emitBom));
        File.SetLastWriteTimeUtc(path, stamp);
        using (var sequential = csv.ToDataReader(path))
            Assert.Throws<InvalidDataException>(() => Collect(sequential));

        using (var sync = csv.WithIndexedLookup(path, index, "Aé\n\"B\""))
            Assert.That(Collect(sync), Is.EqualTo(new[] { "1", "4" }));
        await using var asyncRows = csv.WithIndexedLookup(path, index, "Aé\n\"B\"");
        var matches = new List<string>();
        while (await asyncRows.ReadAsync())
            matches.Add(asyncRows.GetString(0));
        Assert.That(matches, Is.EqualTo(new[] { "1", "4" }));
    }

    [Test]
    public void PartitionPruningFiltersAndPaginationPreserveSourceRowNumbers()
    {
        var first = Write("first.csv", "Id,Key\na,other\n# comment\nb,other\n");
        var selected = Write("selected.csv", "Id,Key\na,other\n# before\nb,wanted\nc,wanted\nd,other\n# between\ne,wanted");
        var rejected = Write("rejected.csv", "Id,Key\nrejected,wanted\n");
        var query = Builder().WithDialect(d => d.WithCommentChar('#')).WithRowNumber().WithSkip(1).WithTake(1)
            .FromFiles([first, selected, rejected]).WithPartitions(p => p.FromPath(path => path != rejected)).Build();
        var index = new HashIndex(new IndexKey<string>(1));
        query.WithPartitionFilter(keep => keep).WithFilter("Id", "no matches").BuildIndexes(index);
        Assert.That(File.Exists(rejected + ".hash.idx"), Is.False);

        using var rows = query.WithIndexedLookup(index, "wanted").WithPartitionFilter(keep => keep)
            .WithFilter("Id", id => !id.SequenceEqual("c")).ToDataReader();
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.GetString(1), Is.EqualTo("e"));
        Assert.That(rows.GetInt32(0), Is.EqualTo(7));
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void HeaderlessDecimalLookupUsesCanonicalHashesAndSourceOrdinals()
    {
        var path = Write("headerless.csv", "a,1.00\nb,2\nc,1.0\nd,0\n");
        var csv = Builder().WithDialect(d => d.WithoutHeader()).Build();
        var index = new HashIndex(new IndexKey<decimal>(1));
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, 1m);
        Assert.That(Collect(rows), Is.EqualTo(new[] { "a", "c" }));
    }

    [TestCase("")]
    [TestCase("Id,Key\n")]
    [TestCase("Id,Key\none,NULL\ntwo")]
    public void EmptySidecarsCanBeReloaded(string content)
    {
        var path = Write("empty.csv", content);
        var csv = Builder().WithDialect(d => d.WithHeader(content.Length != 0).WithNullSequence("NULL")).Build();
        var index = new HashIndex(new IndexKey<int>(1));
        csv.BuildIndexes(path, index);
        using var rows = csv.WithIndexedLookup(path, index, 1);
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void MissingSidecarRequiresOptInAndOptionalLookupDoesNotCreateIt()
    {
        var path = Write("missing.csv", "Id,Key\none,1\ntwo,2\n");
        var csv = Builder().Build();
        var index = new HashIndex(new IndexKey<int>("Key")).WithOptionalIndex();
        using (var strict = csv.WithIndexedLookup(path, index.WithRequiredIndex(), 2))
            Assert.Throws<FileNotFoundException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index, 2))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "two" }));
        Assert.That(File.Exists(path + ".hash.idx"), Is.False);
    }

    [TestCase("checksum")]
    [TestCase("version")]
    [TestCase("unreadable")]
    [TestCase("length")]
    [TestCase("timestamp")]
    [TestCase("key")]
    [TestCase("settings")]
    public void SupportedSourcesRejectInvalidSidecarsUnderEitherPolicy(string failure)
    {
        var path = Write("invalid.csv", "Id,Key\none,1\ntwo,2\n");
        var csv = Builder().Build();
        var index = new HashIndex(new IndexKey<int>("Key"));
        csv.BuildIndexes(path, index);
        var sidecar = path + ".hash.idx";
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
            case "key":
                index = new HashIndex(new IndexKey<int>(0));
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
    public void Utf16BomAndSharedHeadersRequireOptionalScan()
    {
        var path = Write("utf16.csv", "Id,Key\none,1\ntwo,2\n", Encoding.Unicode);
        var csv = Builder().Build();
        var index = new HashIndex(new IndexKey<int>("Key"));
        Assert.Throws<NotSupportedException>(() => csv.BuildIndexes(path, index));
        using (var strict = csv.WithIndexedLookup(path, index, 2))
            Assert.Throws<NotSupportedException>(() => strict.Read());
        using (var scan = csv.WithIndexedLookup(path, index.WithOptionalIndex(), 2))
            Assert.That(Collect(scan), Is.EqualTo(new[] { "two" }));

        var utf8 = Write("shared-header.csv", "Id,Key\none,1\ntwo,2\n");
        var query = Builder().WithDialect(d => d.WithHeaderRepeat(false)).FromFiles([utf8])
            .WithPartitions(p => p.FromPath(_ => true)).Build();
        Assert.Throws<NotSupportedException>(() => query.BuildIndexes(index));
        Assert.Throws<NotSupportedException>(() => query.WithIndexedLookup(index, 2));
        using var shared = query.WithIndexedLookup(index.WithOptionalIndex(), 2).ToDataReader();
        Assert.That(Collect(shared), Is.EqualTo(new[] { "two" }));
    }

    [Test]
    public void RuntimeParserIsUsedForBuildAndLookupAndExplicitFormatOverridesIt()
    {
        var path = Write("dates.csv", "Id,Key\nrecent,19/05/2026\nold,01/01/2025\nother,15/05/2026\n");
        var csv = Builder().WithParsers(p => p.WithParser<DateOnly>(static value =>
            DateOnly.ParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture))).Build();
        var index = new HashIndex(new IndexKey<DateOnly>("Key"));
        var date = new DateOnly(2026, 5, 19);
        csv.BuildIndexes(path, index);
        using (var indexed = csv.WithIndexedLookup(path, index, date))
            Assert.That(Collect(indexed), Is.EqualTo(new[] { "recent" }));
        var plain = Builder().Build();
        using (var incompatible = plain.WithIndexedLookup(path, index.WithOptionalIndex(), date))
            Assert.Throws<InvalidDataException>(() => incompatible.Read());

        var explicitFormat = new HashIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"));
        csv.BuildIndexes(path, explicitFormat);
        using var explicitRows = plain.WithIndexedLookup(path, explicitFormat, date);
        Assert.That(Collect(explicitRows), Is.EqualTo(new[] { "recent" }));
    }

    [Test]
    public void OpenReaderRequiresOptionalLookupAndTransfersOwnership()
    {
        using var stream = new MemoryStream("Id,Key\none,01/01/2025\nmissing\ntwo,15/05/2026\n"u8.ToArray());
        var input = Builder().Build().ToDataReader(stream);
        var index = new HashIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"));
        var date = new DateOnly(2026, 5, 15);
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index, date));
        using (var rows = input.WithIndexedLookup(index.WithOptionalIndex(), date))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "two" }));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public async Task SavedSidecarIsUsableInAFreshProcess()
    {
        const string childPath = "POCKET_CSV_HASH_TEST_SOURCE";
        var csv = Builder().Build();
        var index = new HashIndex(new IndexKey<string>("Key", comparison: StringComparison.OrdinalIgnoreCase));
        if (Environment.GetEnvironmentVariable(childPath) is { } existing)
        {
            using var rows = csv.WithIndexedLookup(existing, index, "café");
            Assert.That(Collect(rows), Is.EqualTo(new[] { "1", "3" }));
            return;
        }

        var path = Write("cross-process.csv", "Id,Key\n1,CAFÉ\n2,other\n3,Café\n");
        csv.BuildIndexes(path, index);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(HashIndexTest).Assembly.Location);
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add($"FullyQualifiedName={typeof(HashIndexTest).FullName}.{nameof(SavedSidecarIsUsableInAFreshProcess)}");
        start.Environment[childPath] = path;
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!child.HasExited)
                child.Kill();
            throw;
        }
        Assert.That(child.ExitCode, Is.Zero, $"Child lookup failed: {await output}{await errors}");
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

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

    private sealed class ConstantHashKey(IIndexKey key) : IIndexKey
    {
        public Type KeyType => key.KeyType;
        string? IIndexKey.ColumnName => key.ColumnName;
        int? IIndexKey.ColumnOrdinal => key.ColumnOrdinal;
        string IIndexKey.CompatibilityId => key.CompatibilityId;
        string IIndexKey.HashCompatibility => "test-constant-hash";
        ulong IIndexKey.GetStableHash(object value) => 0;
        int IIndexKey.Compare(object left, object right) => key.Compare(left, right);
        object IIndexKey.Parse(ReadOnlySpan<char> text) => key.Parse(text);
    }
}
