using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class StableFieldLayoutTests
{
    [Test]
    public async Task ArrayObjectsUseFirstLabelsAndKeepScalarConversions(
        [Values] bool stable, [Values] bool async)
    {
        const string content = """
            [{"\u0069d":1,"label":"first\n","id":99},{"label":"two","id":2,"renamed":100}]
            """;
        var builder = new JsonReaderBuilder().WithParserOptimizations(new(BufferSize: 3));
        if (stable) builder.WithStableFieldLayout();
        using var reader = builder.Build().ToDataReader(Stream(content));

        Assert.That(await Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("first\n"));
        Assert.That(reader.GetInt32(2), Is.EqualTo(99));

        Assert.That(await Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.EqualTo(stable ? 0 : 1));
        Assert.That(reader.GetName(2), Is.EqualTo(stable ? "id" : "renamed"));
        Assert.That(reader.GetString(0), Is.EqualTo("two"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(2));
        Assert.That(await Read(), Is.False);

        async Task<bool> Read() => async ? await reader.ReadAsync() : reader.Read();
    }

    [TestCase("""[{"id":1,"ignored":0},{"id":2}]""")]
    [TestCase("""[{"id":1,"ignored":0},{"id":2,"ignored":0,"extra":0}]""")]
    public async Task SourceCountIsCheckedBeforeProjection(string content)
    {
        using var reader = new JsonReaderBuilder().WithStableFieldLayout()
            .WithProjection(p => p.Property("id")).Build().ToDataReader(Stream(content));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(async () => await reader.ReadAsync(),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("property count"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    [Test]
    public void ProjectionUsesFirstSourceOrdinals()
    {
        const string content = """[{"other":0,"id":1,"id":10},{"other":0,"renamed":2,"id":20}]""";
        using var reader = new JsonReaderBuilder().WithStableFieldLayout()
            .WithOrderedProjection(p => p.Property("id")).Build().ToDataReader(Stream(content));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
    }

    [TestCase("{}", 1)]
    [TestCase("""{"id":1}""", 1)]
    [TestCase("[{},{}]", 2)]
    [TestCase("[]", 0)]
    public void StandaloneAndEmptyObjectsAreSupported(string content, int rows)
    {
        using var reader = new JsonReaderBuilder().WithStableFieldLayout().Build().ToDataReader(Stream(content));
        var count = 0;
        while (reader.Read()) count++;
        Assert.That(count, Is.EqualTo(rows));
    }

    [TestCase("""[{},{"id":2}]""")]
    [TestCase("""[{"id":1},{"bad\q":2}]""")]
    [TestCase("""[{"id":1},{"\uD800":2}]""")]
    [TestCase("""[{"id":"first"},{"id":"bad\q"}]""")]
    [TestCase("""[{"id":1},{"id":2,}]""")]
    public void LaterObjectsStillValidateSyntaxAndLayout(string content)
    {
        using var reader = new JsonReaderBuilder().WithStableFieldLayout().Build().ToDataReader(Stream(content));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void StableSourceReusesItsTwoMetadataTrees()
    {
        const string content = """[{"obj":{"id":1}},{"obj":{"id":2}},{"obj":{"id":3}}]""";
        using var stream = new StreamReader(Stream(content));
        using var source = new JsonRecordSource(stream, new JsonProfile { StableFieldLayout = true });
        source.IsEndOfFile(out var first, out _);
        var fields = first.FieldSpans;
        source.IsEndOfFile(out var second, out _);
        Assert.That(second.FieldSpans, Is.Not.SameAs(fields));
        source.IsEndOfFile(out var third, out _);
        Assert.That(third.FieldSpans, Is.SameAs(fields));
        Assert.That(third.Slice(0).ToString(), Is.EqualTo("""{"id":3}"""));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StableArrayKeepsRawReadAllocationsBounded(bool async)
    {
        const int count = 4_096;
        var document = "[" + string.Join(',', Enumerable.Repeat("""{"\u0069d":1,"obj":{"items":[1,2]}}""", count)) + "]";
        var data = Encoding.UTF8.GetBytes(document);
        var format = new JsonReaderBuilder().WithStableFieldLayout().Build();
        await ReadAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = await ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(rows, Is.EqualTo(count));
        Assert.That(allocated, Is.LessThan(64L * count));

        async Task<int> ReadAll()
        {
            using var reader = format.ToDataReader(new MemoryStream(data));
            var rows = 0;
            while (async ? await reader.ReadAsync() : reader.Read()) rows++;
            return rows;
        }
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));
}
