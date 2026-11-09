using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class FileNamePartitionTest
{
    [Test]
    public void RuntimePatternUsesWholeFilenameAndPreservesAcceptedSourceOrder()
    {
        var country = string.Concat("coun", "try");
        var paths = new[] { "orders-{BE}-2026.csv", "orders-{FR}-2026.csv", "orders-{BE}-2026-extra.csv" }
            .Select(name => Path.Combine("parent", name)).ToArray();
        var opened = new List<string>();
        var query = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .FromFiles(paths, path =>
            {
                opened.Add(path);
                if (path == paths[1])
                    throw new AssertionException("Rejected file was opened.");
                return new MemoryStream(Encoding.UTF8.GetBytes("OrderId\n42\n"));
            })
            .WithPartitions(p => p.FromFileName($"orders-{{{{{{{country}}}}}}}-{{year}}.csv")).Build()
            .WithPartitionFilter(PartitionCondition.Equal(country, "BE"),
                PartitionCondition.NotEqual("year", "2025"));

        var descriptors = query.EnumerateFiles().ToArray();
        Assert.That(descriptors.Select(d => d.Path), Is.EqualTo(new[] { paths[0], paths[2] }));
        Assert.That(descriptors.Select(d => d.Partition["year"]), Is.EqualTo(new[] { "2026", "2026-extra" }));
        Assert.That(descriptors.Select(d => d.Partition[country]), Is.All.EqualTo("BE"));
        using var reader = query.ToDataReader();
        Assert.That(opened, Is.Empty);
        Assert.That(reader.Read(), Is.True);
        Assert.That(opened, Is.EqualTo(new[] { paths[0] }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.False);
        Assert.That(opened, Is.EqualTo(new[] { paths[0], paths[2] }));
    }

    [Test]
    public async Task AllRejectedFilesRemainUnopened()
    {
        var query = new CsvReaderBuilder().FromFiles(new[] { "orders-BE.csv", "orders-FR.csv" },
            _ => throw new AssertionException("Rejected file was opened."))
            .WithPartitions(p => p.FromFileName("orders-{country}.csv")).Build()
            .WithPartitionFilter(PartitionCondition.Equal("country", "GB"));

        Assert.That(query.EnumerateFiles(), Is.Empty);
        await using var reader = query.ToDataReader();
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase("orders.csv")]
    [TestCase("orders-{}.csv")]
    [TestCase("orders-{invalid key}.csv")]
    [TestCase("orders-{year}-{year}.csv")]
    [TestCase("orders-{country}{year}.csv")]
    [TestCase("orders-{year.csv")]
    [TestCase("orders-}-{year}.csv")]
    [TestCase("parent/orders-{year}.csv")]
    public void InvalidPatternsAreRejectedDuringConfiguration(string pattern)
        => Assert.Throws<ArgumentException>(() => new CsvReaderBuilder().FromFiles(Array.Empty<string>())
            .WithPartitions(p => p.FromFileName(pattern)));

    [TestCase("Orders-BE-2026.csv")]
    [TestCase("orders-BE-2026.csv.bak")]
    [TestCase("orders--2026.csv")]
    [TestCase("orders-BE-.csv")]
    public void NonmatchingNamesAreReportedWithoutOpening(string name)
    {
        var query = new CsvReaderBuilder().FromFiles(new[] { name },
            _ => throw new AssertionException("Nonmatching file was opened."))
            .WithPartitions(p => p.FromFileName("orders-{country}-{year}.csv")).Build();

        Assert.That(Assert.Throws<FormatException>(() => query.EnumerateFiles().ToArray())!.Message,
            Does.Contain(name));
    }
}
