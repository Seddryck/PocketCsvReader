using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class JsonTableReaderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadsNamedRowsAndPreservesOwnedValues(bool async)
    {
        const string content = """
            [
              ["\u0069d", "name", "details", "active"],
              [1, "\u00c5da", {"roles":[null,"admin"]}, true],
              [2, null, [null,"L\u0069n"], false]
            ]
            """;
        using var stream = new ChunkedStream(content, async);
        using var reader = SmallReader().ToDataReader(stream);

        Assert.That(await Read(), Is.True);
        var name = reader.GetOrdinal("name");
        var owned = reader.GetString(name);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "id", "name", "details", "active" }));
        Assert.That(reader.RowCount, Is.EqualTo(1));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(owned, Is.EqualTo("Åda"));
        Assert.That(reader.GetRawString(name), Is.EqualTo("\"\\u00c5da\""));
        Assert.That(reader.GetString(2), Is.EqualTo("""{"roles":[null,"admin"]}"""));
        Assert.That(reader.GetBoolean(3), Is.True);

        Assert.That(await Read(), Is.True);
        Assert.That(reader.RowCount, Is.EqualTo(2));
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.IsDBNull(name), Is.True);
        Assert.That(reader.GetValue(name), Is.EqualTo(DBNull.Value));
        Assert.That(reader.GetArray(2), Is.EqualTo(new object?[] { null, "Lin" }));
        Assert.That(owned, Is.EqualTo("Åda"));
        Assert.That(await Read(), Is.False);
        Assert.That(reader.FieldCount, Is.EqualTo(4));

        Task<bool> Read() => async ? reader.ReadAsync() : Task.FromResult(reader.Read());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StreamsRowsLargerThanTheBufferWithoutReadingToEnd(bool async)
    {
        var cell = new string('x', 389);
        var content = "[[\"name\"],[\"" + cell + "\"]," + string.Join(',', Enumerable.Repeat("[\"later\"]", 2000)) + "]";
        using var stream = new ChunkedStream(content, async);
        using var reader = SmallReader().ToDataReader(stream);

        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(cell));
        Assert.That(stream.BytesRead, Is.LessThan(content.Length));
    }

    [TestCase("")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("[null]")]
    [TestCase("[[false]]")]
    [TestCase("[[\"id\",\"\\u0069d\"]]")]
    [TestCase("[[\"id\"],{}]")]
    [TestCase("[[\"id\"],[]]")]
    [TestCase("[[\"id\"],[1,2]]")]
    [TestCase("[[\"id\"],[1,]]")]
    [TestCase("[[\"id\"],[{\"nested\":01}]]")]
    [TestCase("[[\"id\"],[\"bad\\q\"]]")]
    [TestCase("[[\"id\"],[1]")]
    [TestCase("[[\"id\"],[1],]")]
    [TestCase("[[\"id\"],[1]] null")]
    public async Task RejectsInvalidTablesInBothReadModes(string content)
    {
        using var syncReader = SmallReader().ToDataReader(new ChunkedStream(content, asyncOnly: false));
        var syncError = Assert.Throws<InvalidDataException>(() =>
        {
            while (syncReader.Read()) { }
        });

        using var asyncReader = SmallReader().ToDataReader(new ChunkedStream(content, asyncOnly: true));
        var asyncError = await CaptureError();
        Assert.That(syncError!.Message, Does.Contain("position"));
        Assert.That(asyncError.Message, Is.EqualTo(syncError.Message));

        async Task<InvalidDataException> CaptureError()
        {
            try
            {
                while (await asyncReader.ReadAsync()) { }
            }
            catch (InvalidDataException exception)
            {
                return exception;
            }
            throw new AssertionException("Reading an invalid table should throw.");
        }
    }

    [Test]
    public void HeaderOnlyTableExposesColumnsWithoutReturningARow()
    {
        using var reader = SmallReader().ToDataReader(Input("""[["id","name"]]"""));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.Zero);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetName(1), Is.EqualTo("name"));
    }

    [Test]
    public void ZeroColumnHeaderAcceptsEmptyRows()
    {
        using var reader = SmallReader().ToDataReader(Input("[[],[]]"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void SupportsJsoncAndSharedMaterializers()
    {
        const string content = """[/* header */ ["id","name",], [1,"Ada",],]""";
        var profile = new JsonProfile(allowComments: true, allowTrailingCommas: true);
        var reader = new JsonTableReader(profile);
        var row = reader.ToDictionaries(Input(content)).Single();
        Assert.That(row["id"], Is.EqualTo("1"));
        Assert.That(row["name"], Is.EqualTo("Ada"));

        using var table = reader.ToDataTable(Input(content));
        Assert.That(table.Rows[0]["name"], Is.EqualTo("Ada"));
    }

    [Test]
    public void GeneralJsonReaderStillExposesEachArrayAsOneField()
    {
        using var reader = new JsonReader().ToDataReader(Input("""[["id"],[1]]"""));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.GetString(0), Is.EqualTo("""["id"]"""));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("[1]"));
    }

    [Test]
    public void TableSourceReusesCurrentRowMetadata()
    {
        using var stream = new StreamReader(Input("""[["id","name"],[1,"Ada"],[2,"Lin"]]"""));
        using var source = new JsonRecordSource(stream, JsonProfile.Default, table: true);
        source.IsEndOfFile(out _, out _);
        source.IsEndOfFile(out var first, out _);
        var fields = first.FieldSpans;
        source.IsEndOfFile(out var second, out _);
        Assert.That(second.FieldSpans, Is.SameAs(fields));
        Assert.That(second.Slice(1).ToString(), Is.EqualTo("Lin"));
        Assert.That(source.TableHeader, Is.EqualTo(new[] { "id", "name" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RawReadsKeepAllocationsBounded(bool async)
    {
        const int count = 4096;
        var content = """[["id","name","note"],""" + string.Join(',', Enumerable.Repeat("""[1,"\u00c5da","example"]""", count)) + "]";
        var data = Encoding.UTF8.GetBytes(content);
        var format = new JsonTableReader();
        await ReadAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = await ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(rows, Is.EqualTo(count));
        // A record wrapper alone costs 80 bytes per row. Allow 32 bytes per row
        // for reader setup and buffers while catching wrappers and field array copies.
        Assert.That(allocated, Is.LessThan(32L * count));

        async Task<int> ReadAll()
        {
            using var reader = format.ToDataReader(new MemoryStream(data));
            var read = 0;
            while (async ? await reader.ReadAsync() : reader.Read()) read++;
            return read;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadsEveryHeaderlessRowWithSuppliedColumns(bool async)
    {
        using var stream = new ChunkedStream("""[[1,"apple"],[2,"pear"]]""", async);
        using var reader = new JsonTableReader(JsonProfile.Default, ["id", "name"]).ToDataReader(stream);

        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetName(0), Is.EqualTo("id"));
        var name = reader.GetOrdinal("name");
        Assert.That(name, Is.EqualTo(1));
        Assert.That(stream.BytesRead, Is.Zero);

        Assert.That(await Read(), Is.True);
        Assert.That(reader.RowCount, Is.EqualTo(1));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(name), Is.EqualTo("apple"));

        Assert.That(await Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.GetString(name), Is.EqualTo("pear"));
        Assert.That(await Read(), Is.False);

        Task<bool> Read() => async ? reader.ReadAsync() : Task.FromResult(reader.Read());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EmptyHeaderlessTableKeepsSuppliedColumns(bool async)
    {
        using var stream = new ChunkedStream("[]", async);
        using var reader = new JsonTableReader(JsonProfile.Default, ["id", "name"]).ToDataReader(stream);

        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.Zero);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetName(1), Is.EqualTo("name"));
    }

    [TestCase("[[1]]", 1, false)]
    [TestCase("[[1,2,3]]", 3, false)]
    [TestCase("[[1,2],[3]]", 1, true)]
    [TestCase("[[1,2],[3,4,5]]", 3, true)]
    public async Task RejectsWrongHeaderlessRowWidths(string content, int width, bool startsWithValidRow)
    {
        var format = new JsonTableReader(JsonProfile.Default, ["id", "name"]);
        using var syncReader = format.ToDataReader(new ChunkedStream(content, asyncOnly: false));
        using var asyncReader = format.ToDataReader(new ChunkedStream(content, asyncOnly: true));
        if (startsWithValidRow)
        {
            Assert.That(syncReader.Read(), Is.True);
            Assert.That(await asyncReader.ReadAsync(), Is.True);
        }

        var syncError = Assert.Throws<InvalidDataException>(() => syncReader.Read());
        var asyncError = await Assert.ThrowsAsync<InvalidDataException>(async () => await asyncReader.ReadAsync());
        Assert.That(syncError!.Message, Does.Contain($"Expected 2 values but found {width}."));
        Assert.That(asyncError!.Message, Is.EqualTo(syncError.Message));
    }

    [Test]
    public void RejectsInvalidSuppliedColumnNames()
    {
        Assert.That(() => new JsonTableReader(JsonProfile.Default, null!),
            Throws.ArgumentNullException);
        Assert.That(() => new JsonTableReader(JsonProfile.Default, ["id", null!]),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("columnNames"));
        Assert.That(() => new JsonTableReader(JsonProfile.Default, ["id", "id"]),
            Throws.ArgumentException.With.Message.Contains("Duplicate column name 'id'"));
    }

    [Test]
    public void SnapshotsSuppliedColumnNamesAndKeepsReaderMetadataIndependent()
    {
        string[] names = ["id", "ID"];
        var format = new JsonTableReader(JsonProfile.Default, names);
        names[0] = "changed";
        using var first = format.ToDataReader(Input("[]"));
        using var second = format.ToDataReader(Input("[]"));

        Assert.That(first.GetName(0), Is.EqualTo("id"));
        first.Fields![0] = "changed";
        Assert.That(second.GetName(0), Is.EqualTo("id"));
        Assert.That(second.GetOrdinal("ID"), Is.EqualTo(1));
    }

    private static JsonTableReader SmallReader()
        => new(new JsonProfile(parserOptimizations: JsonProfile.DefaultParserOptimizations with { BufferSize = 8 }));

    private static MemoryStream Input(string content) => new(Encoding.UTF8.GetBytes(content));

    private sealed class ChunkedStream(string content, bool asyncOnly) : Stream
    {
        private readonly MemoryStream _content = Input(content);

        public long BytesRead => _content.Position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
            => asyncOnly ? throw new InvalidOperationException("Synchronous reads are not allowed.")
                : _content.Read(buffer[..Math.Min(7, buffer.Length)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _content.ReadAsync(buffer[..Math.Min(7, buffer.Length)], cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _content.Dispose();
            base.Dispose(disposing);
        }
    }
}
