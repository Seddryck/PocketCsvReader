using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class PartitionedFileQueryTest
{
    [Test]
    public void HiveDiscoveryPrunesBeforeOpeningAndInheritsFilenamePartitions()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var belgium = Directory.CreateDirectory(Path.Combine(root.FullName, "year=2026", "country=BE"));
            var france = Directory.CreateDirectory(Path.Combine(root.FullName, "year=2026", "country=FR", "invalid="));
            var accepted = Path.Combine(belgium.FullName, "events-01.ndjson");
            File.WriteAllText(accepted, "{\"id\":42,\"status\":\"open\"}\n{\"id\":43,\"status\":\"closed\"}\n");
            File.WriteAllText(Path.Combine(france.FullName, "events-02.ndjson"), "rejected");

            var opened = new List<string>();
            var query = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).Build()
                .FromDirectory(root.FullName, "*.ndjson", path =>
                {
                    opened.Add(path);
                    return File.OpenRead(path);
                })
                .HiveStyle("events-{batch}.ndjson")
                .WithPartitionFilter(PartitionCondition.Equal("country", "BE"))
                .WithPartitionFilter(p => p["year"] == "2026" && p["batch"] == "01")
                .WithFilter("status", "open");

            var file = query.EnumerateFiles().Single();
            Assert.That(file.Path, Is.EqualTo(accepted));
            Assert.That(file.Partition["batch"], Is.EqualTo("01"));
            using var reader = query.ToDbDataReader();
            Assert.That(opened, Is.Empty);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<int>(reader.GetOrdinal("id")), Is.EqualTo(42));
            Assert.That(reader.Read(), Is.False);
            Assert.That(opened, Is.EqualTo(new[] { accepted }));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task QueryFiltersAndPaginationComposeAcrossFiles(bool useAsync)
    {
        var opened = new List<MemoryStream>();
        var files = new[]
        {
            FileSource.Partitioned("first.ndjson", true, () => Open("{\"id\":1}\n{\"id\":2}\n")),
            FileSource.Partitioned("second.ndjson", true, () => Open("{\"id\":3}\n{\"id\":4}\n")),
            FileSource.Partitioned("unused.ndjson", true, () => throw new AssertionException("Take must stop opening files."))
        };
        var query = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithFilter<int>("id", id => id >= 2).WithSkip(1).WithTake(1).Build().FromFiles(files);
        var even = query.WithFilter<int>("id", id => id % 2 == 0)
            .WithRowFilter(row => row.GetFieldValue<int>("id") < 5);

        await Check(even, expected: 4);
        await Check(query, expected: 3);
        Assert.That(opened, Has.Count.EqualTo(4));
        Assert.That(opened.All(stream => !stream.CanRead), Is.True);

        async Task Check(FlatFileQuery<bool> selected, int expected)
        {
            await using var reader = selected.ToDbDataReader();
            Assert.That(useAsync ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<int>(reader.GetOrdinal("id")), Is.EqualTo(expected));
            Assert.That(useAsync ? await reader.ReadAsync() : reader.Read(), Is.False);
        }

        MemoryStream Open(string contents)
        {
            if (opened.LastOrDefault() is { } previous)
                Assert.That(previous.CanRead, Is.False);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
            opened.Add(stream);
            return stream;
        }
    }

    [Test]
    public async Task RowFilterFailureDisposesTheActiveStream()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":42}\n"));
        var query = new NdjsonReaderBuilder().Build()
            .FromFiles(new[] { FileSource.Partitioned("file.ndjson", true, () => stream) })
            .WithRowFilter(_ => throw new InvalidOperationException("Filter failed."));

        await using var reader = query.ToDataReader();
        Assert.That((await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync()))!.Message,
            Is.EqualTo("Filter failed."));
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(stream.CanRead, Is.False);
    }
}
