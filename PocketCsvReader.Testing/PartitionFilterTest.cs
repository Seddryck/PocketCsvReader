using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class PartitionFilterTest
{
    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    [Test]
    public void TypedFiltersPreserveOrderAndComposeWithRowFilters()
    {
        var opened = new List<string>();
        var secondFilter = new List<string>();
        var files = new[]
        {
            FileSource.Partitioned("rejected", (Country: "FR", Year: 2026),
                () => throw new AssertionException("Rejected opener was called.")),
            File("first", 2026),
            File("wrong-year", 2025),
            File("last", 2026)
        };

        var query = Builder().WithFilter("Keep", "yes").WithRowFilter(row => row.GetFieldValue<int>(2) > 1)
            .FromFiles(files).Build().WithPartitionFilter(p => p.Country == "BE")
            .WithPartitionFilter(p =>
            {
                secondFilter.Add($"{p.Country}-{p.Year}");
                return p.Year == 2026;
            });

        Assert.That(query.EnumerateFiles().Select(f => f.Path), Is.EqualTo(new[] { "first", "last" }));
        Assert.That(secondFilter, Is.EqualTo(new[] { "BE-2026", "BE-2025", "BE-2026" }));
        using var reader = query.ToDataReader();
        Assert.That(opened, Is.Empty);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["Name"], Is.EqualTo("first"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["Name"], Is.EqualTo("last"));
        Assert.That(reader.Read(), Is.False);
        Assert.That(opened, Is.EqualTo(new[] { "first", "last" }));

        PartitionedFile<(string Country, int Year)> File(string name, int year)
            => FileSource.Partitioned(name, ("BE", year), () =>
            {
                opened.Add(name);
                return new MemoryStream(Encoding.UTF8.GetBytes(
                    $"Name,Keep,Quantity\n{name},no,2\n{name},yes,1\n{name},yes,2\n"));
            });
    }

    [Test]
    public async Task NamedConditionsShortCircuitAndOpenNothingWhenAllAreRejected()
    {
        var query = Builder().FromFiles(new[] { File("BE"), File("fr") }).Build()
            .WithPartitionFilter(PartitionCondition.NotEqual("country", "BE"),
                PartitionCondition.Equal("country", "FR"))
            .WithPartitionFilter(_ => throw new AssertionException("The first filter rejected this file."));

        Assert.That(query.EnumerateFiles(), Is.Empty);
        await using var reader = query.ToDataReader();
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(reader.Read(), Is.False);

        static PartitionedFile<NamedPartitions> File(string country)
            => FileSource.Partitioned(country, new NamedPartitions([new("country", country)]),
                () => throw new AssertionException("Rejected opener was called."));
    }

    [Test]
    public void MissingKeysAreReportedOnlyIfReached()
    {
        var query = Builder().FromFiles(new[]
        {
            FileSource.Partitioned("orders", new NamedPartitions([new("year", "2026")]))
        }).Build();

        Assert.That(query.WithPartitionFilter(PartitionCondition.Equal("year", "2025"),
            PartitionCondition.NotEqual("missing", "FR")).EnumerateFiles(), Is.Empty);

        foreach (var condition in new[] { PartitionCondition.Equal("Year", "2026"), PartitionCondition.NotEqual("missing", "FR") })
        {
            var filtered = query.WithPartitionFilter(condition);
            var error = Assert.Throws<KeyNotFoundException>(() => filtered.EnumerateFiles().ToArray());
            Assert.That(error!.Message, Does.Contain(condition.Key));
        }

        Assert.Throws<KeyNotFoundException>(() => query.WithPartitionFilter(p => p["missing"] == "BE")
            .EnumerateFiles().ToArray());
    }

    [Test]
    public void PredicateErrorsPropagateAndSharedHeadersAreRejectedBeforeDiscovery()
    {
        var failure = new FormatException("Invalid partition");
        var query = Builder().FromFiles(new[] { FileSource.Partitioned("orders", 2026) }).Build()
            .WithPartitionFilter(_ => throw failure);
        Assert.That(Assert.Throws<FormatException>(() => query.EnumerateFiles().ToArray()), Is.SameAs(failure));
        using var reader = query.ToDataReader();
        Assert.That(Assert.Throws<FormatException>(() => reader.Read()), Is.SameAs(failure));

        var sharedHeader = Builder().WithDialect(d => d.WithoutHeaderRepeat()).FromFiles(new[] { "orders" })
            .WithPartitions(p => p.FromPath<int>(_ => throw new AssertionException("Discovery started."))).Build()
            .WithPartitionFilter(_ => true);
        Assert.Throws<InvalidOperationException>(() => sharedHeader.ToDataReader());
        Assert.Throws<InvalidOperationException>(() => sharedHeader.ToDbDataReader());
    }

    [Test]
    public void ConditionsAreSnapshottedAndRejectedOrUnvisitedInputsStayUnopened()
    {
        var files = new[]
        {
            FileSource.Partitioned("skip", new NamedPartitions([new("country", "FR")]),
                () => throw new AssertionException("Rejected opener was called.")),
            FileSource.Partitioned("first", new NamedPartitions([new("country", "BE")]),
                () => new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n"))),
            FileSource.Partitioned("later", new NamedPartitions([new("country", "BE")]),
                () => throw new AssertionException("Unvisited opener was called."))
        };
        var conditions = new[] { PartitionCondition.Equal("country", "BE") };
        var query = Builder().FromFiles(files).Build().WithPartitionFilter(conditions);
        conditions[0] = PartitionCondition.Equal("country", "FR");

        Assert.That(query.EnumerateFiles().Select(f => f.Path), Is.EqualTo(new[] { "first", "later" }));
        using var reader = query.ToDataReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["Name"], Is.EqualTo("Ada"));
    }
}
