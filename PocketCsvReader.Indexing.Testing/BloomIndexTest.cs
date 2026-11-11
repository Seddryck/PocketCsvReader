using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class BloomIndexTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("pocket-bloom-index-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task NegativeDoesNotOpenTheCsvAndPartitionFiltersRunBeforeSidecars()
    {
        var negative = Write("negative.csv", "Key,Id\nzzz,x\nyyy,y\n");
        var first = Write("first.csv", "Key,Id\na,1\ntarget,2\n");
        var last = Write("last.csv", "Key,Id\nother,3\ntarget,4\n");
        var rejected = Write("rejected.csv", "Key,Id\ntarget,5\n");
        var query = Builder().WithRowNumber().WithSkip(1).WithTake(1)
            .FromFiles([negative, first, last, rejected])
            .WithPartitions(p => p.FromPath(path => path != rejected)).Build();
        var index = new BloomIndex(new IndexKey<string>("Key"));
        query.WithPartitionFilter(keep => keep).WithFilter("Id", "never").BuildIndexes(index);
        Assert.That(File.Exists(rejected + ".bloom.idx"), Is.False);

        var lookup = query.WithIndexedLookup(index, "target").WithPartitionFilter(keep => keep);
        using var locked = File.Open(negative, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using (var sync = lookup.ToDataReader())
        {
            Assert.That(sync.Read(), Is.True);
            Assert.That(sync.GetInt32(0), Is.EqualTo(6));
            Assert.That(sync.GetString(2), Is.EqualTo("4"));
            Assert.That(sync.Read(), Is.False);
        }

        await using var asyncReader = lookup.ToDataReader();
        Assert.That(await asyncReader.ReadAsync(), Is.True);
        Assert.That(asyncReader.GetInt32(0), Is.EqualTo(6));
        Assert.That(await asyncReader.ReadAsync(), Is.False);
    }

    [TestCase(StringComparison.OrdinalIgnoreCase, "äpfel", "ÄPFEL", "Äpfel")]
    [TestCase(StringComparison.InvariantCultureIgnoreCase, "café", "CAFÉ", "Café")]
    [TestCase(StringComparison.Ordinal, "A", "A", "A")]
    public void UnsortedDuplicatesAndMissingOrNullKeysMatchTheSequentialPredicate(
        StringComparison comparison, string first, string second, string requested)
    {
        var path = Write("values.csv", $"Id,Key\n1,z\n2,{first}\n3,NULL\n4,a\n5,{second}\n6\n7,{first}\n");
        var builder = Builder().WithDialect(d => d.WithNullSequence("NULL"));
        var query = builder.FromFiles([path]).WithPartitions(p => p.FromPath(_ => true)).Build();
        var selected = query.WithFilter("Id", id => !id.SequenceEqual("7"));
        var index = new BloomIndex(new IndexKey<string>(1, comparison: comparison));

        var expected = new List<string>();
        using (var sequential = selected.ToDataReader())
        {
            while (sequential.Read())
            {
                if (sequential.FieldCount > 1 && !sequential.IsDBNull(1)
                    && string.Equals(sequential.GetString(1), requested, comparison))
                    expected.Add(sequential.GetString(0));
            }
        }

        var optional = index.WithOptionalIndex();
        using (var scan = selected.WithIndexedLookup(optional, requested).ToDbDataReader())
            Assert.That(Collect(scan), Is.EqualTo(expected));
        Assert.That(File.Exists(path + ".bloom.idx"), Is.False);

        selected.BuildIndexes(index);
        using var indexed = query.WithIndexedLookup(index, requested).WithFilter("Id", id => !id.SequenceEqual("7"))
            .ToDbDataReader();
        Assert.That(expected, Is.EqualTo(new[] { "2", "5" }));
        Assert.That(Collect(indexed), Is.EqualTo(expected));
        using var nullKey = query.WithIndexedLookup(index, "NULL").ToDbDataReader();
        Assert.That(nullKey.Read(), Is.False);
    }

    [Test]
    public void AFalsePositiveOpensTheFileAndStillRequiresExactEquality()
    {
        var path = Write("saturated.csv", "Key\n" + string.Concat(Enumerable.Range(0, 1024).Select(i => $"{i}\n")));
        var csv = Builder().Build();
        var index = new BloomIndex(new IndexKey<int>("Key"), maxBytes: 32);
        csv.BuildIndexes(path, index);
        // Every lane of this one-block filter is full. An absent value is
        // therefore a reproducible false positive, not a probabilistic test.
        Assert.That(File.ReadAllBytes(path + ".bloom.idx").Skip(64).Take(32), Is.All.EqualTo(byte.MaxValue));
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var lockedLookup = csv.WithIndexedLookup(path, index, -1))
            Assert.Throws<IOException>(() => lockedLookup.Read());

        using var rows = csv.WithIndexedLookup(path, index, -1);
        Assert.That(rows.Read(), Is.False);
    }

    [Test]
    public void EqualDecimalsWithDifferentScalesShareAHash()
    {
        var path = Write("decimals.csv", "Key,Id\n1.000,a\n-0.000,b\n1.0,c\n");
        var csv = Builder().Build();
        var index = new BloomIndex(new IndexKey<decimal>("Key"));
        csv.BuildIndexes(path, index);
        using var ones = csv.WithIndexedLookup(path, index, 1m);
        Assert.That(Collect(ones, 1), Is.EqualTo(new[] { "a", "c" }));
        using var zero = csv.WithIndexedLookup(path, index, 0m);
        Assert.That(Collect(zero, 1), Is.EqualTo(new[] { "b" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExistingInvalidSidecarsFailInBothModes(bool optional)
    {
        var path = Write("invalid.csv", "Key\nBob\n");
        var sidecar = path + ".bloom.idx";
        var csv = Builder().Build();
        var required = new BloomIndex(new IndexKey<string>("Key", comparison: StringComparison.OrdinalIgnoreCase));
        var index = optional ? required.WithOptionalIndex() : required;
        csv.BuildIndexes(path, index);
        var valid = File.ReadAllBytes(sidecar);

        using (File.Open(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var unreadable = csv.WithIndexedLookup(path, index, "Bob"))
            Assert.Throws<IOException>(() => unreadable.Read());

        var bytes = (byte[])valid.Clone();
        bytes[^33] ^= 1;
        File.WriteAllBytes(sidecar, bytes);
        AssertInvalid(csv, path, index, "corrupt");

        bytes = (byte[])valid.Clone();
        bytes[7]++;
        File.WriteAllBytes(sidecar, bytes);
        AssertInvalid(csv, path, index, "format");

        File.WriteAllBytes(sidecar, valid);
        var changedHash = new BloomIndex(new HashVersionKey(required.Key));
        AssertInvalid(csv, path, optional ? changedHash.WithOptionalIndex() : changedHash, "incompatible");
        var differentComparison = new BloomIndex(new IndexKey<string>("Key"));
        AssertInvalid(csv, path, optional ? differentComparison.WithOptionalIndex() : differentComparison, "incompatible");
        AssertInvalid(Builder().WithDialect(d => d.WithNullSequence("Bob")).Build(), path, index, "incompatible");

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(-1));
        AssertInvalid(csv, path, index, "stale");
        csv.BuildIndexes(path, index);
        File.AppendAllText(path, "Alice\n");
        AssertInvalid(csv, path, index, "stale");
    }

    [Test]
    public void MissingSidecarOrUnsupportedReaderOnlyScansWhenOptional()
    {
        var path = Write("no-index.csv", "Key\nBob\n");
        var csv = Builder().Build();
        var index = new BloomIndex(new IndexKey<string>("Key"));
        var optional = index.WithOptionalIndex();
        using (var required = csv.WithIndexedLookup(path, optional.WithRequiredIndex(), "Bob"))
            Assert.Throws<FileNotFoundException>(() => required.Read());
        using (var fallback = csv.WithIndexedLookup(path, optional, "Bob"))
            Assert.That(Collect(fallback), Is.EqualTo(new[] { "Bob" }));
        Assert.That(File.Exists(path + ".bloom.idx"), Is.False);

        // A query opener is a different source even if the descriptor's path
        // happens to point to a filesystem CSV with an existing sidecar.
        csv.BuildIndexes(path, index);
        var custom = Builder().FromFiles([path], _ => new MemoryStream("Key\nAlice\n"u8.ToArray()))
            .WithPartitions(p => p.FromPath(_ => true)).Build();
        Assert.Throws<NotSupportedException>(() => custom.BuildIndexes(index));
        using (var required = custom.WithIndexedLookup(index, "Alice").ToDataReader())
            Assert.Throws<NotSupportedException>(() => required.Read());
        using (var fallback = custom.WithIndexedLookup(optional, "Alice").ToDbDataReader())
            Assert.That(Collect(fallback), Is.EqualTo(new[] { "Alice" }));

        var unsupportedPath = Write("unicode.csv", "Key·Id\nBob·1\n");
        var unsupported = Builder().WithDialect(d => d.WithDelimiter('·')).Build();
        File.WriteAllText(unsupportedPath + ".bloom.idx", "unusable");
        Assert.Throws<NotSupportedException>(() => unsupported.BuildIndexes(unsupportedPath, index));
        Assert.Throws<NotSupportedException>(() => unsupported.WithIndexedLookup(unsupportedPath, index, "Bob"));
        using var scan = unsupported.WithIndexedLookup(unsupportedPath, optional, "Bob");
        Assert.That(Collect(scan, 1), Is.EqualTo(new[] { "1" }));
    }

    [Test]
    public void RegisteredParserAndExplicitSettingsUseTheSameRulesForBuildAndLookup()
    {
        var path = Write("dates.csv", "Key,Id\n31/12/2026,z\n17/05/2026,a\n01/01/2026,b\n17/05/2026,c\n");
        var expected = new DateOnly(2026, 5, 17);
        var index = new BloomIndex(new IndexKey<DateOnly>("Key"));
        ParseSpan<DateOnly> parseDate = ParseDate;
        var csv = Builder().WithParsers(p => p.WithParser(parseDate)).Build();
        csv.BuildIndexes(path, index);
        using (var rows = csv.WithIndexedLookup(path, index, expected))
            Assert.That(Collect(rows, 1), Is.EqualTo(new[] { "a", "c" }));

        using (var incompatible = Builder().Build().WithIndexedLookup(path, index.WithOptionalIndex(), expected))
            Assert.Throws<InvalidDataException>(() => incompatible.Read());

        var explicitIndex = new BloomIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"));
        var other = Builder().WithParsers(p => p.WithParser<DateOnly>(
            static (ReadOnlySpan<char> _) => throw new AssertionException("Explicit key settings take precedence."))).Build();
        csv.BuildIndexes(path, explicitIndex);
        using var explicitRows = other.WithIndexedLookup(path, explicitIndex, expected);
        Assert.That(Collect(explicitRows, 1), Is.EqualTo(new[] { "a", "c" }));

        static DateOnly ParseDate(ReadOnlySpan<char> text)
            => DateOnly.ParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    [Test]
    public void OpenDataReaderUsesDefinitionAndTransfersOwnership()
    {
        using var table = new DataTable();
        table.Columns.Add("Key", typeof(string));
        table.Rows.Add("17/05/2026");
        table.Rows.Add(DBNull.Value);
        table.Rows.Add("01/01/2026");
        var input = table.CreateDataReader();
        var index = new BloomIndex(new IndexKey<DateOnly>("Key", format: "dd/MM/yyyy"));
        var date = new DateOnly(2026, 5, 17);
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index, date));
        using (var rows = input.WithIndexedLookup(index.WithOptionalIndex(), date))
            Assert.That(Collect(rows), Is.EqualTo(new[] { "17/05/2026" }));
        Assert.That(input.IsClosed, Is.True);
    }

    [Test]
    public async Task SavedSidecarIsUsableInAFreshProcess()
    {
        const string childPath = "POCKET_CSV_BLOOM_TEST_SOURCE";
        var csv = Builder().Build();
        var index = new BloomIndex(new IndexKey<string>("Key", comparison: StringComparison.OrdinalIgnoreCase));
        if (Environment.GetEnvironmentVariable(childPath) is { } existing)
        {
            // The child only looks up the sidecar. A process-randomized hash
            // would reject a key that was present when the parent built it.
            using var rows = csv.WithIndexedLookup(existing, index, "café");
            Assert.That(Collect(rows, 1), Is.EqualTo(new[] { "1", "3" }));
            return;
        }

        var path = Write("cross-process.csv", "Key,Id\nCAFÉ,1\nother,2\nCafé,3\n");
        csv.BuildIndexes(path, index);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(BloomIndexTest).Assembly.Location);
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add($"FullyQualifiedName={typeof(BloomIndexTest).FullName}.{nameof(SavedSidecarIsUsableInAFreshProcess)}");
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static List<string> Collect(IDataReader reader, int ordinal = 0)
    {
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(reader.GetString(ordinal));
        return rows;
    }

    private sealed class HashVersionKey(IIndexKey key) : IIndexKey
    {
        public Type KeyType => key.KeyType;
        string? IIndexKey.ColumnName => key.ColumnName;
        int? IIndexKey.ColumnOrdinal => key.ColumnOrdinal;
        string IIndexKey.CompatibilityId => key.CompatibilityId;
        string IIndexKey.BloomHashCompatibility => "incompatible-hash-v2";
        ulong IIndexKey.GetBloomHash(object value) => ~key.GetBloomHash(value);
        int IIndexKey.Compare(object left, object right) => key.Compare(left, right);
        object IIndexKey.Parse(ReadOnlySpan<char> text) => key.Parse(text);
    }

    private static void AssertInvalid(CsvReader csv, string path, BloomIndex index, string problem)
    {
        using var rows = csv.WithIndexedLookup(path, index, "bob");
        Assert.That(Assert.Throws<InvalidDataException>(() => rows.Read())!.Message, Does.Contain(problem));
    }
}
