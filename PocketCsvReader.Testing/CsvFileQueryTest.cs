using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvFileQueryTest
{
    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    [Test]
    public void DescriptorsAreReplayableAndDatabaseHasRowsRequestsARecord()
    {
        var opened = new List<string>();
        PartitionedFile<int> File(string name, int year) => FileSource.Partitioned(name, year, () =>
        {
            opened.Add(name);
            return new TrackingStream($"Name\n{name}\n");
        });

        var query = Builder().FromFiles(new[] { File("first.csv", 2026), File("second.csv", 2025) }).Build();

        Assert.That(query.EnumerateFiles().Select(f => (f.Path, f.Partition)),
            Is.EqualTo(new[] { ("first.csv", 2026), ("second.csv", 2025) }));
        using (query.ToDataReader()) { }
        using var reader = query.ToDbDataReader();
        Assert.That(opened, Is.Empty);
        Assert.That(reader.HasRows, Is.True);
        Assert.That(opened, Is.EqualTo(new[] { "first.csv" }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["Name"], Is.EqualTo("first.csv"));
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        using var replay = query.ToDataReader();
        Assert.That(replay.Read(), Is.True);
        Assert.That(opened, Is.EqualTo(new[] { "first.csv", "first.csv" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectorAndOpenersRunOnDemandAndInputsAreDisposedInOrder(bool useAsync)
    {
        var selected = new List<string>();
        var first = new TrackingStream("Name\nAda\n");
        var second = new TrackingStream("Name\nGrace\n");
        var query = Builder().FromFiles(new[] { "first", "second", "third" }, path => path switch
        {
            "first" => first,
            "second" when first.Disposals == 1 => second,
            _ => throw new AssertionException($"Unexpected open of {path}.")
        })
        .WithPartitions(p => p.FromPath(path =>
        {
            selected.Add(path);
            return path.Length;
        }))
        .Build();

        var reader = query.ToDataReader();
        try
        {
            Assert.That(selected, Is.Empty);
            Assert.That(await Read(), Is.True);
            Assert.That(reader["Name"], Is.EqualTo("Ada"));
            Assert.That(await Read(), Is.True);
            Assert.That(reader["Name"], Is.EqualTo("Grace"));
            Assert.That(selected, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(second.Disposals, Is.Zero);
        }
        finally
        {
            if (useAsync)
                await reader.DisposeAsync();
            else
                reader.Dispose();
        }

        Assert.That(first.Disposals, Is.EqualTo(1));
        Assert.That(second.Disposals, Is.EqualTo(1));

        Task<bool> Read() => useAsync ? reader.ReadAsync() : Task.FromResult(reader.Read());
    }

    [Test]
    public void EmptyAndInvalidInputsAreHandledAtTheirBoundary()
    {
        Assert.Throws<ArgumentException>(() => FileSource.Partitioned(" ", 2026));
        Assert.Throws<ArgumentNullException>(() => FileSource.Partitioned<string>("file", null!));
        Assert.Throws<ArgumentNullException>(() => Builder().FromFiles(null!));
        Assert.Throws<ArgumentNullException>(() => Builder().FromFiles<int>(null!));

        using var empty = Builder().FromFiles(Array.Empty<PartitionedFile<int>>()).Build().ToDataReader();
        Assert.That(empty.Read(), Is.False);

        var invalid = Builder().FromFiles(new PartitionedFile<int>[] { null! }).Build();
        Assert.Throws<ArgumentException>(() => invalid.EnumerateFiles().ToArray());
        using var reader = invalid.ToDataReader();
        Assert.Throws<ArgumentException>(() => reader.Read());
        Assert.That(reader.IsClosed, Is.True);

        var invalidPath = Builder().FromFiles(new[] { " " }).WithPartitions(p => p.FromPath<object>(_ =>
            throw new AssertionException("Invalid paths should not reach the selector."))).Build();
        Assert.Throws<ArgumentException>(() => invalidPath.EnumerateFiles().ToArray());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedReadClosesTheActiveStream(bool useAsync)
    {
        var stream = new TrackingStream("Name\nAda\n");
        var failure = new InvalidOperationException("row predicate failed");
        var query = Builder().WithRowFilter(_ => throw failure)
            .FromFiles(new[] { FileSource.Partitioned("first", 2026, () => stream) }).Build();

        await using var reader = query.ToDataReader();
        var thrown = useAsync
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync())
            : Assert.Throws<InvalidOperationException>(() => reader.Read());
        Assert.That(thrown, Is.SameAs(failure));
        Assert.That(stream.Disposals, Is.EqualTo(1));
        Assert.That(reader.IsClosed, Is.True);
    }

    private sealed class TrackingStream(string text) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public int Disposals { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Disposals++;
            base.Dispose(disposing);
        }
    }
}
