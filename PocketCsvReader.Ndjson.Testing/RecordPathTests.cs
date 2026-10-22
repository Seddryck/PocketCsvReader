using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

[TestFixture(false)]
[TestFixture(true)]
public class RecordPathTests(bool ndjson)
{
    private const string Family = """{"outside":0,"child":[{"grandchildren":["Ada","Grace"]},{"grandchildren":["Linus"]}]}""";

    private static Stream StreamFor(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private IAsyncDataReader Create(string content, string path, bool asyncOnly = false)
    {
        Stream stream = StreamFor(content);
        if (asyncOnly)
            stream = new AsyncOnlyStream(stream);
        var options = new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false);
        return ndjson
            ? new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n")).WithRecordPath(path).WithParserOptimizations(options).Build().ToDataReader(stream)
            : new JsonReaderBuilder().WithRecordPath(path).WithParserOptimizations(options).Build().ToDataReader(stream);
    }

    private object?[] GetArray(IAsyncDataReader reader)
        => reader is JsonDataReader json ? json.GetArray(0) : ((NdjsonDataReader)reader).GetArray(0);

    [TestCase(false)]
    [TestCase(true)]
    public async Task RepeatedWildcards_SelectScalarRowsInOrder(bool asynchronous)
    {
        using var reader = Create(Family, "$.child[*].grandchildren[*]", asynchronous);
        var values = new List<string>();
        while (asynchronous ? await reader.ReadAsync() : reader.Read())
        {
            Assert.That(reader.FieldCount, Is.EqualTo(1));
            Assert.That(reader.GetName(0), Is.Empty);
            values.Add(reader.GetString(0));
        }
        Assert.That(values, Is.EqualTo(new[] { "Ada", "Grace", "Linus" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TerminalMember_PreservesArrayRows(bool asynchronous)
    {
        using var reader = Create(Family, "$.child[*].grandchildren", asynchronous);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(GetArray(reader), Is.EqualTo(new[] { "Ada", "Grace" }));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(GetArray(reader), Is.EqualTo(new[] { "Linus" }));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MixedMatches_ExposeObjectsScalarsArraysAndNull(bool asynchronous)
    {
        using var reader = Create("""{"items":[{"name":"Ada"},"line\nfeed",[1,2],null,{},42,true]}""", "$.items[*]", asynchronous);
        async Task Next() => Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        await Next();
        Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Ada"));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetOrdinal("items"));
        await Next();
        Assert.That(reader.GetString(0), Is.EqualTo("line\nfeed"));
        await Next();
        Assert.That(GetArray(reader), Is.EqualTo(new[] { "1", "2" }));
        await Next();
        Assert.That(reader.IsDBNull(0), Is.True);
        await Next();
        Assert.That(reader.FieldCount, Is.Zero);
        await Next();
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        await Next();
        Assert.That(reader.GetBoolean(0), Is.True);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [TestCase("$.missing")]
    [TestCase("$.child.grandchildren")]
    [TestCase("$.outside[*]")]
    [TestCase("$.child[*].missing[*]")]
    public async Task NoMatches_ProducesNoRows(string path)
    {
        using var sync = Create(Family, path);
        Assert.That(sync.Read(), Is.False);
        using var asyncReader = Create(Family, path, true);
        Assert.That(await asyncReader.ReadAsync(), Is.False);
    }

    [TestCase("$['child.with.dot']['a[b]']")]
    [TestCase("$[\"child.with.dot\"][\"a[b]\"]")]
    [TestCase("$['child.with.dot']['a\\u005bb]']")]
    public void BracketNames_SelectLiteralPunctuation(string path)
    {
        using var reader = Create("""{"child.with.dot":{"a[b]":"value"}}""", path);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("value"));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase("$['']", "{\"\":7}")]
    [TestCase("$['a\\'b']", "{\"a'b\":7}")]
    [TestCase("$.é", "{\"é\":7}")]
    [TestCase("$['😀']", "{\"\\uD83D\\uDE00\":7}")]
    public void MemberNames_AreDecodedAndComparedExactly(string path, string content)
    {
        using var reader = Create(content, path);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
    }

    [TestCase("[]")]
    [TestCase("[1,2]")]
    [TestCase("null")]
    [TestCase("\"Ada\"")]
    [TestCase("{\"id\":1}")]
    public void Root_SelectsOneCompleteValue(string content)
    {
        using var reader = Create(content, "$");
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase("")]
    [TestCase("child")]
    [TestCase("root.child")]
    [TestCase("$.child[0]")]
    [TestCase("$..child")]
    [TestCase("$.*")]
    [TestCase("$.")]
    [TestCase("$.0")]
    [TestCase("$[?(@.id)]")]
    [TestCase("$[1:2]")]
    [TestCase("$['a','b']")]
    [TestCase("$['a'")]
    [TestCase("$['\\q']")]
    [TestCase("$['\\uD800']")]
    [TestCase("$[*]trailing")]
    public void InvalidSyntax_IsRejectedAtBuild(string path)
    {
        var exception = ndjson
            ? Assert.Throws<ArgumentException>(() => new NdjsonReaderBuilder().WithRecordPath(path).Build())
            : Assert.Throws<ArgumentException>(() => new JsonReaderBuilder().WithRecordPath(path).Build());
        Assert.That(exception!.Message, Does.Contain(path));
        Assert.Throws<ArgumentException>(() => new JsonProfile(recordPath: path));
        Assert.Throws<ArgumentException>(() => new NdjsonProfile(new NdjsonDialectDescriptor(), recordPath: path));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DuplicateMatchingMembers_ThrowBeforeChoosingEither(bool asynchronous)
    {
        using var reader = Create("""{"child":1,"ch\u0069ld":2}""", "$.child", asynchronous);
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync());
        else
            Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void LaterAmbiguity_IsDiscoveredLazily()
    {
        using var reader = Create("""{"items":[{"child":1},{"child":2,"child":3}]}""", "$.items[*].child");
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [TestCase("{\"child\":[1,]}")]
    [TestCase("{\"child\":[1}")]
    [TestCase("{\"child\":true} garbage")]
    [TestCase("{\"child\":\"\\q\"}")]
    public async Task MalformedJson_ReportsPosition(string content)
    {
        using var reader = Create(content, "$.child");
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("position"));
        using var asyncReader = Create(content, "$.child", true);
        Assert.That((await Assert.ThrowsAsync<InvalidDataException>(async () => await asyncReader.ReadAsync()))!.Message, Does.Contain("position"));
    }

    [Test]
    public async Task MultipleRootsOrBranches_SkipUnmatchedValues()
    {
        var content = ndjson
            ? "{\"child\":[1,2]}\n{}\n\n{\"child\":[3]}\n"
            : "[{\"child\":[1,2]},{},{\"child\":[3]}]";
        using var reader = Create(content, ndjson ? "$.child[*]" : "$[*].child[*]", true);
        var values = new List<int>();
        while (await reader.ReadAsync()) values.Add(reader.GetInt32(0));
        Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void ProjectionAndSchema_ApplyToSelectedObjects()
    {
        const string content = """{"ignored":0,"child":[{"ignored":1,"name":"Ada"},{"name":"Grace"}]}""";
        using var stream = StreamFor(content);
        using var reader = ndjson
            ? (IAsyncDataReader)new NdjsonReaderBuilder().WithRecordPath("$.child[*]")
                .WithProjection(p => p.Property("name")).WithSchema(s => s.Named()).Build().ToDataReader(stream)
            : new JsonReaderBuilder().WithRecordPath("$.child[*]")
                .WithProjection(p => p.Property("name")).WithSchema(s => s.Named()).Build().ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Ada"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public async Task Cancellation_IsObservedBetweenMatches()
    {
        using var reader = Create(Family, "$.child[*].grandchildren[*]", true);
        Assert.That(await reader.ReadAsync(), Is.True);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(cancellation.Token));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
    }

    [Test]
    public async Task CustomParser_AppliesToSelectedFields()
    {
        using var stream = StreamFor("{\"child\":[\"yes\",\"no\"]}");
        using var reader = ndjson
            ? (IAsyncDataReader)new NdjsonReaderBuilder().WithRecordPath("$.child[*]")
                .WithParsers(p => p.WithParser((string value) => value == "yes")).Build().ToDataReader(stream)
            : new JsonReaderBuilder().WithRecordPath("$.child[*]")
                .WithParsers(p => p.WithParser((string value) => value == "yes")).Build().ToDataReader(stream);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetBoolean(0), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetBoolean(0), Is.False);
    }

    [Test]
    public void BufferSizeConstructor_PreservesRecordPath()
    {
        using var stream = StreamFor(Family + new string(' ', 200));
        using var reader = ndjson
            ? (IAsyncDataReader)new NdjsonReader(new NdjsonProfile(new NdjsonDialectDescriptor(), recordPath: "$.child[*].grandchildren[*]"), 1).ToDataReader(stream)
            : new JsonReader(new JsonProfile(recordPath: "$.child[*].grandchildren[*]"), 1).ToDataReader(stream);
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        Assert.That(values, Is.EqualTo(new[] { "Ada", "Grace", "Linus" }));
    }

    private sealed class AsyncOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous read.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

public class NdjsonRecordPathIntegrationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task StableShapeAndOrderedProjection_ApplyAfterSelection(bool asynchronous)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"child\":[{\"name\":\"Ada\",\"id\":1},{\"name\":\"Grace\",\"id\":2}]}"));
        using var reader = new NdjsonReaderBuilder().WithRecordPath("$.child[*]")
            .WithStableObjectShape().WithOrderedProjection(p => p.Property("id")).Build().ToDataReader(stream);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommentsAndCustomTerminators_KeepPerValueRoots(bool asynchronous)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# ignored|{\"child\":[1,2]} # comment|{}|{\"child\":[3]}|"));
        using var reader = new NdjsonReaderBuilder().WithRecordPath("$.child[*]")
            .WithDialect(d => d.WithLineTerminator("|").WithCommentChar('#')).Build().ToDataReader(stream);
        var values = new List<int>();
        while (asynchronous ? await reader.ReadAsync() : reader.Read()) values.Add(reader.GetInt32(0));
        Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
    }
}
