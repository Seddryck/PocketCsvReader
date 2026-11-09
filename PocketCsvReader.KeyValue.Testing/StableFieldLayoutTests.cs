using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class StableFieldLayoutTests
{
    [Test]
    public async Task StableLayoutReusesLabelsAndFirstDuplicateByOrdinal(
        [Values] bool logfmt, [Values] bool async)
    {
        string content = logfmt
            ? "id=1 id=duplicate message=\"first\\n\"\nrenamed=2 other=duplicate id="
            : "id:1\tid:duplicate\tmessage:first\nrenamed:2\tother:duplicate\tid:";
        using var reader = Build(logfmt).ToDataReader(Stream(content));
        Assert.That(await Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.GetString(1), Is.EqualTo("duplicate"));
        Assert.That(reader.GetString(2), Is.EqualTo(logfmt ? "first\n" : "first"));

        Assert.That(await Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.GetName(1), Is.EqualTo("id"));
        Assert.That(reader.GetName(2), Is.EqualTo("message"));
        Assert.That(reader.TryGetOrdinal("renamed", out _), Is.False);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.GetString(2), Is.Empty);
        Assert.That(reader.IsDBNull(2), Is.False);
        Assert.That(await Read(), Is.False);

        async Task<bool> Read() => async ? await reader.ReadAsync() : reader.Read();
    }

    [TestCase(false, "id:1\tignored:0\nid:2")]
    [TestCase(false, "id:1\tignored:0\nid:2\tignored:0\textra:0")]
    [TestCase(true, "id=1 ignored=0\nid=2")]
    [TestCase(true, "id=1 ignored=0\nid=2 ignored=0 extra=0")]
    public async Task SourceCountIsCheckedBeforeProjection(bool logfmt, string content)
    {
        using var reader = Build(logfmt, projection: true).ToDataReader(Stream(content));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetInt32(reader.GetOrdinal("Id")), Is.EqualTo(1));
        Assert.That(reader.IsDBNull(1), Is.True);
        Assert.That(async () => await reader.ReadAsync(),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("field count"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    [TestCase(false, "other:1\tid:2\tid:3\nother:4\trenamed:5\tmissing:6")]
    [TestCase(true, "other=1 id=2 id=3\nother=4 renamed=5 missing=6")]
    public void ProjectionCachesFirstSourceOrdinalAndMissingKey(bool logfmt, string content)
    {
        using var reader = Build(logfmt, projection: true).ToDataReader(Stream(content));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.IsDBNull(1), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("Id"), Is.Zero);
        Assert.That(reader.GetInt32(0), Is.EqualTo(5));
        Assert.That(reader.IsDBNull(1), Is.True);
    }

    [TestCase(false, "\n\nid:1")]
    [TestCase(true, "\n\nid=1")]
    public void EmptyFirstRecordEstablishesZeroFields(bool logfmt, string content)
    {
        using var reader = Build(logfmt).ToDataReader(Stream(content));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.Zero);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(false, "id:1\tignored:0\nid:2\tbad label:0")]
    [TestCase(true, "id=1 ignored=0\nid=2 ignored=\"bad\\q\"")]
    [TestCase(true, "id=1 ignored=0\nid=2 \"quoted key\"=0")]
    public void LaterUnprojectedFieldsStillValidateSyntax(bool logfmt, string content)
    {
        using var reader = Build(logfmt, projection: true).ToDataReader(Stream(content));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("position"));
    }

    [Test]
    public async Task StableProjectionKeepsRawReadAllocationsBounded([Values] bool logfmt, [Values] bool async)
    {
        const int count = 4_096;
        var line = logfmt ? "id=1 unused=foo message=bar\n" : "id:1\tunused:foo\tmessage:bar\n";
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, count)));
        var format = Build(logfmt, projection: true, bufferSize: 4096);
        await ReadAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = await ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(rows, Is.EqualTo(count));
        // Includes stream/reader setup and cross-buffer bookkeeping. Rejects a per-row record wrapper.
        Assert.That(allocated, Is.LessThan(64L * count));

        async Task<int> ReadAll()
        {
            using var reader = format.ToDataReader(new MemoryStream(data));
            var rows = 0;
            while (async ? await reader.ReadAsync() : reader.Read()) rows++;
            return rows;
        }
    }

    private static KeyValueReader Build(bool logfmt, bool projection = false, int bufferSize = 3)
        => logfmt
            ? Configure(new LogfmtReaderBuilder(), projection, bufferSize).Build()
            : Configure(new LtsvReaderBuilder(), projection, bufferSize).Build();

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    private static TBuilder Configure<TBuilder>(KeyValueReaderBuilder<TBuilder> builder, bool projection, int bufferSize)
        where TBuilder : KeyValueReaderBuilder<TBuilder>
    {
        builder.WithStableFieldLayout().WithParserOptimizations(new(BufferSize: bufferSize));
        if (projection) builder.WithProjection(p => p.Column("id", "Id").Column("missing", "Missing"));
        return (TBuilder)builder;
    }
}
