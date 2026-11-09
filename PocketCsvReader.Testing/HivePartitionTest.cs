using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class HivePartitionTest
{
    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    [Test]
    public async Task PruningSkipsListingRejectedSubtreesAndDefersOpeningAcceptedFiles()
    {
        var root = "data";
        var old = Path.Combine(root, "year=2025");
        var current = Path.Combine(root, "year=2026");
        var france = Path.Combine(current, "country=FR");
        var belgium = Path.Combine(current, "country=BE");
        var reverseCountry = Path.Combine(root, "country=NL");
        var reverseYear = Path.Combine(reverseCountry, "year=2026");
        var files = new[]
        {
            Path.Combine(belgium, "orders-001.csv"), Path.Combine(belgium, "orders-002.csv"),
            Path.Combine(reverseYear, "orders-003.csv")
        };
        var visits = new List<string>();
        var opened = new List<string>();
        var tree = new HivePartitionSource(root,
            path => path == root ? [old, current, reverseCountry]
                : path == current ? [france, belgium]
                : path == reverseCountry ? [reverseYear] : [],
            path =>
            {
                visits.Add(path);
                if (path == old || path == france)
                    throw new AssertionException($"Pruned directory was listed: {path}");
                return files.Where(file => Path.GetDirectoryName(file) == path);
            });
        var query = new CsvDirectoryBuilder(Builder(), tree, path =>
        {
            opened.Add(path);
            if (path != files[0])
                throw new AssertionException("A later file was opened.");
            return new MemoryStream(Encoding.UTF8.GetBytes("OrderId\n42\n"));
        }).WithPartitions(p => p.HiveStyle("orders-{batch}.csv")).Build()
            .WithPartitionFilter(PartitionCondition.Equal("year", "2026"),
                PartitionCondition.NotEqual("country", "FR"))
            .WithPartitionFilter(PartitionCondition.NotEqual("batch", "004"));

        Assert.That(visits, Is.Empty);
        var discovered = query.EnumerateFiles().ToArray();
        Assert.That(discovered.Select(file => file.Path), Is.EqualTo(files));
        Assert.That(discovered.Select(file => file.Partition["country"]), Is.EqualTo(new[] { "BE", "BE", "NL" }));
        Assert.That(discovered.Select(file => file.Partition["batch"]), Is.EqualTo(new[] { "001", "002", "003" }));
        Assert.That(visits, Is.EqualTo(new[] { root, current, belgium, reverseCountry, reverseYear }));
        Assert.That(opened, Is.Empty);
        visits.Clear();

        await using (var reader = query.ToDataReader())
        {
            Assert.That(visits, Is.Empty);
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader["OrderId"], Is.EqualTo("42"));
            Assert.That(opened, Is.EqualTo(new[] { files[0] }));
        }
        Assert.That(visits, Is.EqualTo(new[] { root, current, belgium }));
    }

    [TestCase("year=2025")]
    [TestCase("ye%61r=%32025")]
    public void AllRejectedDirectoriesAreNeverListed(string segment)
    {
        var root = "data";
        var child = Path.Combine(root, segment);
        var tree = new HivePartitionSource(root,
            path => path == root ? [child] : throw new AssertionException("Pruned subdirectories were listed."),
            path => path == root ? [] : throw new AssertionException("Pruned files were listed."));
        var query = new CsvDirectoryBuilder(Builder(), tree, _ => throw new AssertionException("File opened."))
            .HiveStyle().Build()
            .WithPartitionFilter(PartitionCondition.Equal("year", "2026"));

        Assert.That(query.EnumerateFiles(), Is.Empty);
        using var reader = query.ToDataReader();
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void NamedConditionsDoNotMoveAheadOfADelegate()
    {
        var root = "data";
        var child = Path.Combine(root, "year=2025");
        var tree = new HivePartitionSource(root, path => path == root ? [child] : [],
            path => path == child ? [Path.Combine(path, "file.csv")] : []);
        var failure = new FormatException("Delegate ran before the named condition.");
        var query = new CsvDirectoryBuilder(Builder(), tree, null).HiveStyle().Build()
            .WithPartitionFilter(_ => throw failure)
            .WithPartitionFilter(PartitionCondition.Equal("year", "2026"));

        Assert.That(Assert.Throws<FormatException>(() => query.EnumerateFiles().ToArray()), Is.SameAs(failure));
    }

    [Test]
    public void CompleteFilesReportMissingKeysAndDuplicateFilenameCaptures()
    {
        var root = "data";
        var child = Path.Combine(root, "year=2026");
        var tree = new HivePartitionSource(root, path => path == root ? [child] : [],
            path => path == child ? [Path.Combine(path, "orders-2026.csv")] : []);
        var stage = new CsvDirectoryBuilder(Builder(), tree, null);
        var query = stage.HiveStyle().Build().WithPartitionFilter(PartitionCondition.NotEqual("country", "FR"));

        Assert.That(Assert.Throws<KeyNotFoundException>(() => query.EnumerateFiles().ToArray())!.Message,
            Does.Contain("country"));
        Assert.That(Assert.Throws<FormatException>(() => stage.HiveStyle("orders-{year}.csv")
            .Build().EnumerateFiles().ToArray())!.Message, Does.Contain("Duplicate partition key 'year'"));
    }

    [TestCase("year=2026", "Duplicate partition key 'year'")]
    [TestCase("year=2025", "Duplicate partition key 'year'")]
    [TestCase("ye%61r=2026", "Duplicate partition key 'year'")]
    [TestCase("country=", "Invalid Hive partition directory")]
    [TestCase("=BE", "Invalid Hive partition directory")]
    [TestCase("country=%09", "Invalid Hive partition directory")]
    [TestCase("%20=BE", "Invalid Hive partition directory")]
    public void InvalidVisitedSegmentsFailEvenWithoutFiles(string segment, string expected)
    {
        var root = "data";
        var parent = Path.Combine(root, "year=2026");
        var child = Path.Combine(parent, segment);
        var tree = new HivePartitionSource(root,
            path => path == root ? [parent] : path == parent ? [child] : [], _ => []);
        var query = new CsvDirectoryBuilder(Builder(), tree, null).HiveStyle().Build();

        Assert.That(Assert.Throws<FormatException>(() => query.EnumerateFiles().ToArray())!.Message,
            Does.Contain(expected));
    }

    [TestCase("tag=A=B")]
    [TestCase("tag=A%3DB")]
    public void DirectoryBuilderFiltersOnTheDecodedTag(string tagDirectory)
    {
        var temp = Directory.CreateTempSubdirectory("pocketcsv-partitions-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "ignored=root")).FullName;
            var belgium = Directory.CreateDirectory(Path.Combine(root, "ordinary", tagDirectory, "year=2026", "country=BE"));
            var france = Directory.CreateDirectory(Path.Combine(root, "year=2026", "country=FR", "tag=other"));
            var rejected = Directory.CreateDirectory(Path.Combine(root, "year=2025", "tag="));
            var first = Path.Combine(belgium.FullName, "orders.csv");
            var second = Path.Combine(france.FullName, "orders.csv");
            File.WriteAllText(first, "OrderId,Country,Year\n42,BE,2026\n");
            File.WriteAllText(second, "OrderId,Country,Year\n43,FR,2026\n");
            File.WriteAllText(Path.Combine(belgium.FullName, "ignored.txt"), "Not CSV");
            File.WriteAllText(Path.Combine(rejected.FullName, "orders.csv"), "Invalid partition");

            var query = Builder().FromDirectory(root, "*.csv").WithPartitions(p => p.HiveStyle()).Build()
                .WithPartitionFilter(PartitionCondition.Equal("year", "2026"));
            Assert.That(query.EnumerateFiles().Select(file => file.Path), Is.EquivalentTo(new[] { first, second }));

            var descriptor = query.WithPartitionFilter(PartitionCondition.Equal("tag", "A=B")).EnumerateFiles().Single();
            Assert.That(descriptor.Path, Is.EqualTo(first));
            Assert.That(descriptor.Partition["tag"], Is.EqualTo("A=B"));
            Assert.That(descriptor.Partition.Keys, Is.EquivalentTo(new[] { "tag", "year", "country" }));

            using var reader = query.ToDataReader();
            var rows = new List<(string OrderId, string Country, string Year)>();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            Assert.That(rows, Is.EquivalentTo(new[] { ("42", "BE", "2026"), ("43", "FR", "2026") }));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Test]
    public void CountryOnlyFilterReadsBelgianFilesAcrossYears()
    {
        var root = Directory.CreateTempSubdirectory("pocketcsv-partitions-");
        try
        {
            var belgium2025 = WriteOrders("2025", "BE", "41");
            var belgium2026 = WriteOrders("2026", "BE", "42");
            WriteOrders("2025", "FR", "43");

            var query = Builder().FromDirectory(root.FullName, "*.csv").WithPartitions(p => p.HiveStyle()).Build()
                .WithPartitionFilter(PartitionCondition.Equal("country", "BE"));

            Assert.That(query.EnumerateFiles().Select(file => file.Path),
                Is.EquivalentTo(new[] { belgium2025, belgium2026 }));

            using var reader = query.ToDataReader();
            var orders = new List<string>();
            while (reader.Read())
                orders.Add(reader.GetString(0));
            Assert.That(orders, Is.EquivalentTo(new[] { "41", "42" }));
        }
        finally
        {
            root.Delete(recursive: true);
        }

        string WriteOrders(string year, string country, string order)
        {
            var directory = Directory.CreateDirectory(Path.Combine(root.FullName, $"year={year}", $"country={country}"));
            var path = Path.Combine(directory.FullName, "orders.csv");
            File.WriteAllText(path, $"OrderId\n{order}\n");
            return path;
        }
    }

    [TestCase("A%3dB", "A=B")]
    [TestCase("A%2FB+%25", "A/B+%")]
    [TestCase("A%253DB", "A%3DB")]
    [TestCase("A%3GB%4", "A%3GB%4")]
    [TestCase("%C3%A9", "\u00C3\u00A9")]
    [TestCase("__HIVE_DEFAULT_PARTITION__", "__HIVE_DEFAULT_PARTITION__")]
    public void DirectoryValuesUseSinglePassHiveEscaping(string encoded, string expected)
    {
        var root = "data";
        var child = Path.Combine(root, $"tag={encoded}");
        var tree = new HivePartitionSource(root, path => path == root ? [child] : [],
            path => path == child ? [Path.Combine(path, "orders.csv")] : []);
        var query = new CsvDirectoryBuilder(Builder(), tree, null).HiveStyle().Build();

        Assert.That(query.EnumerateFiles().Single().Partition["tag"], Is.EqualTo(expected));
    }

    [TestCase("../*.csv")]
    [TestCase("child\\*.csv")]
    public void DirectoryGlobCannotChangeTheRoot(string glob)
        => Assert.Throws<ArgumentException>(() => Builder().FromDirectory("data", glob));
}
