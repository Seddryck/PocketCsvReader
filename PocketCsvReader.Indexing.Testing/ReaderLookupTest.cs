using System.Data;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Indexing.Testing;

[TestFixture]
public class ReaderLookupTest
{
    [Test]
    public void CsvDataReaderScansUnsortedInputWithTheSameLookupApi()
    {
        var csv = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n")).Build();
        using var input = new MemoryStream("Date,Id\n2026-12-31,1\n2026-05-17,2\n2026-01-01,3\n2026-05-17,4\n"u8.ToArray());
        var index = new SparseIndex(new IndexKey<DateOnly>("Date"), 2).WithOptionalIndex();
        using var rows = csv.ToDataReader(input).WithIndexedLookup(index, new DateOnly(2026, 5, 17));

        var found = new List<int>();
        while (rows.Read())
            found.Add(rows.GetInt32(1));
        Assert.That(found, Is.EqualTo(new[] { 2, 4 }));
    }

    [Test]
    public void NdjsonScansVariableRowShapesAndPreservesFileOrder()
    {
        const string json = """
            {"date":"2026-12-31","id":1}
            {"id":2,"date":"2026-05-17"}
            {"id":3}
            {"id":4,"date":null}
            {"date":"2026-01-01","id":5}
            {"date":"2026-05-17","id":6}
            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var source = Ndjson().ToDataReader(input);
        var index = new SparseIndex(new IndexKey<DateOnly>("date"), 2).WithOptionalIndex();
        using (var rows = source.WithIndexedLookup(index, new DateOnly(2026, 5, 17)))
        {
            var found = new List<int>();
            Assert.That(rows.HasRows, Is.True);
            while (rows.Read())
                found.Add(rows.GetInt32(rows.GetOrdinal("id")));
            Assert.That(found, Is.EqualTo(new[] { 2, 6 }));
        }
        Assert.That(input.CanRead, Is.False);
    }

    [Test]
    public async Task NdjsonFallbackUsesAsyncReadsAndDisposesItsSource()
    {
        var input = new AsyncOnlyStream(Encoding.UTF8.GetBytes("{\"key\":\"z\"}\n{\"key\":\"AbC\"}\n{\"key\":\"abc\"}"));
        var source = Ndjson().ToDataReader(input);
        var index = new SparseIndex(new IndexKey<string>("key", comparison: StringComparison.OrdinalIgnoreCase), 2)
            .WithOptionalIndex();

        await using (var rows = source.WithIndexedLookup(index, "ABC"))
        {
            var found = new List<string>();
            while (await rows.ReadAsync())
                found.Add(rows.GetString(0));
            Assert.That(found, Is.EqualTo(new[] { "AbC", "abc" }));
        }
        Assert.That(input.Disposed, Is.True);
    }

    [Test]
    public async Task StandardDataReaderUsesTypedValuesAcrossResultSets()
    {
        using var first = new DataTable();
        first.Columns.Add("key", typeof(int));
        first.Rows.Add(1);
        using var second = first.Clone();
        second.Rows.Add(3);
        second.Rows.Add(2);
        second.Rows.Add(3);
        using IDataReader input = new DataTableReader([first, second]);
        var index = new SparseIndex(new IndexKey<int>("key"), 2).WithOptionalIndex();
        Assert.Throws<NotSupportedException>(() => input.WithIndexedLookup(index.WithRequiredIndex(), 3));
        using var rows = input.WithIndexedLookup(index, 3);

        Assert.That(rows.HasRows, Is.False);
        Assert.That(await rows.NextResultAsync(), Is.True);
        Assert.That(rows.HasRows, Is.True);
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.GetFieldValue<int>(0), Is.EqualTo(3));
        Assert.That(await rows.ReadAsync(), Is.True);
        Assert.That(rows.Read(), Is.False);
    }

    private static NdjsonReader Ndjson() => new NdjsonReaderBuilder()
        .WithDialect(d => d.WithLineTerminator("\n")).Build();

    private sealed class AsyncOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
