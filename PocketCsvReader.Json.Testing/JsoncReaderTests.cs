using System.Text;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class JsoncReaderTests
{
    private const string Object = """
        { /* names */ "id" /* colon */ : 1/* value */, "text":"literal // /* */",
          "items": [null/* one */, "line\nfeed", true, /* trailing */],
          "ignored": { "nested": [1, {"ok":true,},], }, // end property
        }
        """;

    [Test]
    public void Profile_DefaultAndExistingConstructorRemainStrict()
    {
        Assert.That(new JsonProfile().AllowComments, Is.False);
        using var reader = new JsonReader(new JsonProfile(allowComments: true, allowTrailingCommas: true), bufferSize: 1)
            .ToDataReader(Input("/* before */ {\"id\":1,} // eof"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public async Task CommentsAndTrailingCommas_PreserveValues(
        [Values(1, 2, 7, 65536)] int bufferSize,
        [Values(false, true)] bool async,
        [Values(0, 1, 2)] int projection,
        [Values(false, true)] bool selected)
    {
        var builder = Builder(bufferSize, projection);
        if (selected) builder.WithRecordPath("$.customers[*]");
        var rows = "[/* first */" + Object + ", // between\r\n" + Object + ",/* final */]";
        var document = "// before root\r\n" + (selected ? "{\"customers\":" + rows + ",\"other\":[false,],}" : rows) + "/* after root */ // eof";
        using var stream = new AsyncOnlyWhenRequestedStream(Input(document), async);
        using var reader = builder.Build().ToDataReader(stream);
        for (var row = 0; row < 2; row++)
        {
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.FieldCount, Is.EqualTo(projection == 0 ? 4 : 3));
                Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
                Assert.That(reader.GetString(reader.GetOrdinal("text")), Is.EqualTo("literal // /* */"));
                Assert.That(reader.GetArray(reader.GetOrdinal("items")), Is.EqualTo(new object?[] { null, "line\nfeed", "true" }));
            });
        }
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    private static readonly (string Content, int Rows)[] ValidRoots =
    [
        ("1// eof", 1),
        ("{/* } ] \" delimiters */\"id\":1,}", 1),
        ("{\"id\":1,// } ] \" ignored\n}", 1),
        ("/* before */[1,]/* after */", 1),
        ("true/* after */", 1),
        ("null// eof", 1),
        ("\"// /* */\"// eof", 1),
        ("[1/* after */,true// line\n,null,]// eof", 3),
        ("[/* empty */]/* after */", 0),
        ("{/* empty */}// eof", 1),
        ("{\"url\":\"https://example.test/* literal */\",}", 1),
        ("{/* block /* does not nest */\"id\":1,}", 1),
    ];

    [Test]
    public async Task RootValuesAndEofComments([ValueSource(nameof(ValidRoots))] (string Content, int Rows) example,
        [Values(false, true)] bool async)
    {
        using var reader = Builder(1).Build().ToDataReader(Input(example.Content));
        var rows = 0;
        while (async ? await reader.ReadAsync() : reader.Read()) rows++;
        Assert.That(rows, Is.EqualTo(example.Rows));
    }

    private static readonly string[] InvalidDocuments =
    [
        "[1,,2]", "[,1]", "{,}", "[1,,]", "{\"id\":1,,}", "{\"id\":,}",
        "[1,/* comment */,]", "{\"id\":1,/* comment */,}",
        "/* unfinished", "{}/* unfinished", "[1,/* unfinished", "[1/* unfinished", "[1/",
        "{\"id\":/* unfinished}", "{\"id\":1/* unfinished}", "{\"id\":1/}",
        "[1/* gap */2]", "{\"id\":tr/* gap */ue}", "{\"id\":1./* gap */0}",
        "{\"id\":1e/* gap */2}", "{\"id\":01}", "{\"id\":\"bad\\x\"}", "{\"id\":\"bad\n\"}",
        "{}/* gap */{}", "[]// gap\n[]", "1/* gap */2", "[1,]/* ok */false",
        "[1,", "// no root", "/", "1/", "[1/,]"
    ];

    [Test]
    public async Task InvalidJsonc_IsRejectedWithPosition([ValueSource(nameof(InvalidDocuments))] string content,
        [Values(false, true)] bool async)
    {
        using var reader = Builder(1).Build().ToDataReader(Input(content));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
        Assert.That(exception!.Message, Does.Contain("position"));
    }

    private static readonly string[] InvalidBranches =
    [
        "[1,,2]",
        "{,}",
        "[1/* gap */2]",
        "01",
        "\"bad\\x\"",
        "{\"x\":true,,}",
        "[1/* unterminated ]}",
    ];

    [Test]
    public async Task ProjectionAndSelection_ValidateUnselectedBranches([ValueSource(nameof(InvalidBranches))] string invalid,
        [Values(1, 2)] int projection, [Values(false, true)] bool selected,
        [Values(false, true)] bool async)
    {
        var builder = Builder(1, projection);
        if (selected) builder.WithRecordPath("$.customers[*]");
        var row = "{\"id\":1,\"text\":\"ok\",\"items\":[],\"ignored\":" + invalid + "}";
        var document = selected ? "{\"customers\":[" + row + ",]}" : row;
        using var reader = builder.Build().ToDataReader(Input(document));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
    }

    private static readonly string[] StrictRejections =
    [
        "// before\n{}",
        "{}// after",
        "{/* within */\"id\":1}",
        "{\"id\":1,}",
        "[1,]",
        "{\"items\":[1,]}",
    ];

    [Test]
    public async Task DefaultDialect_RejectsJsonc([ValueSource(nameof(StrictRejections))] string content,
        [Values(false, true)] bool async)
    {
        using var reader = new JsonReader().ToDataReader(Input(content));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
    }

    [Test]
    public async Task Comments_RetainSourcePositions(
        [Values(false, true)] bool async, [Values(false, true)] bool selected)
    {
        const string prefix = "/* before\r\n */[// rows\r\n";
        const string first = "{\"id\":1,},/* between\r\n */";
        var builder = Builder(1).WithPositionTracking();
        var envelope = selected ? "{\"customers\":" : "";
        if (selected) builder.WithRecordPath("$.customers[*]");
        using var reader = builder.Build().ToDataReader(Input(envelope + prefix + first + "{\"id\":2,},]" + (selected ? "}" : "")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(1, 3, envelope.Length + prefix.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 4, envelope.Length + prefix.Length + first.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [Test]
    public async Task RootArray_StreamsBeforeReadingWholeDocument([Values(false, true)] bool async)
    {
        var content = "[/* start */{\"id\":1,}," + string.Concat(Enumerable.Repeat("{\"id\":2,},", 20000)) + "]";
        using var stream = new AsyncOnlyWhenRequestedStream(Input(content), async);
        using var reader = Builder(8).Build().ToDataReader(stream);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(stream.BytesRead, Is.LessThan(content.Length));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    [Test]
    public void Projection_DoesNotAllocateSkippedRecordText()
    {
        // A copied 2 KiB record would cost >4 KiB/row. The 64 byte/row budget
        // allows buffer-crossing bookkeeping but catches per-row field arrays or text copies.
        var row = "{\"id\":1,\"ignored\":\"" + new string('x', 2048) + "\",/* comment */},";
        var content = Encoding.UTF8.GetBytes("[" + string.Concat(Enumerable.Repeat(row, 1000)) + "]");
        var profile = new JsonProfile(allowComments: true, allowTrailingCommas: true, projectedProperties: ["id"]);
        for (var pass = 0; pass < 2; pass++)
        {
            using var text = new StreamReader(new MemoryStream(content));
            using var source = new JsonRecordSource(text, profile);
            source.IsEndOfFile(out _, out var firstState);
            Assert.That(firstState, Is.EqualTo(RecordState.Record));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var rows = 1;
            while (true)
            {
                source.IsEndOfFile(out _, out var state);
                if (state == RecordState.Eof) break;
                rows++;
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(rows, Is.EqualTo(1000));
            if (pass == 1) Assert.That(allocated, Is.LessThan(64 * 1000));
        }
    }

    [Test]
    public void Projection_ReusesBorrowedBuffersAndRetainsMaterializedValues()
    {
        const string content = "[{\"id\":\"first\",/* comment */},{\"id\":\"next\",},]";
        using var text = new StreamReader(Input(content));
        using var source = new JsonRecordSource(text, new JsonProfile(allowComments: true, allowTrailingCommas: true, projectedProperties: ["id"]));
        source.IsEndOfFile(out var first, out _);
        var fields = first.FieldSpans;
        var retainedValue = first.Slice(0).ToString();
        Assert.That(MemoryMarshal.TryGetArray(first.AsMemory().Span, out var firstMemory), Is.True);
        source.IsEndOfFile(out var second, out _);
        Assert.That(MemoryMarshal.TryGetArray(second.AsMemory().Span, out var secondMemory), Is.True);
        Assert.That(secondMemory.Array, Is.SameAs(firstMemory.Array));
        Assert.That(second.FieldSpans, Is.SameAs(fields));
        Assert.That(second.Slice(0).ToString(), Is.EqualTo("next"));
        Assert.That(retainedValue, Is.EqualTo("first"));
    }

    [Test]
    public void JsoncFileExtension_DoesNotRelaxDefaultDialect()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"strict-default-{Guid.NewGuid():N}.jsonc");
        File.WriteAllText(path, "{\"id\":1,}");
        try
        {
            using var reader = new JsonReader().ToDataReader(path);
            Assert.Throws<InvalidDataException>(() => reader.Read());
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task RecordPath_SelectsRootArraysAndScalars([Values(false, true)] bool async)
    {
        using var reader = Builder(1).WithRecordPath("$").Build()
            .ToDataReader(Input("/* root */[1,2,/* end */]// eof"));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetArray(0), Is.EqualTo(new[] { "1", "2" }));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);

        using var scalar = Builder(1).WithRecordPath("$").Build().ToDataReader(Input("42// eof"));
        Assert.That(async ? await scalar.ReadAsync() : scalar.Read(), Is.True);
        Assert.That(scalar.GetInt32(0), Is.EqualTo(42));
        Assert.That(async ? await scalar.ReadAsync() : scalar.Read(), Is.False);
    }

    [Test]
    public async Task TrailingCommentLookahead_PreservesCurrentRecord([Values(false, true)] bool async)
    {
        using var reader = Builder(32).Build().ToDataReader(Input("{\"text\":\"retained\",}/* " + new string('x', 4096) + " */"));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("retained"));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
    }

    [Test]
    public async Task RecordPath_ValidatesBranchesOutsideSelectedRows(
        [ValueSource(nameof(InvalidBranches))] string invalid, [Values(false, true)] bool async)
    {
        using var reader = Builder(1).WithRecordPath("$.customers[*]").Build().ToDataReader(
            Input("{\"customers\":[{\"id\":1,},],\"ignored\":" + invalid + "}"));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
    }

    [Test]
    public async Task UnterminatedComment_ReportsOriginalSourceOffset(
        [Values(false, true)] bool selected, [Values(false, true)] bool async)
    {
        const string document = "/* preface */\r\n{\"id\":1,/* unfinished";
        var builder = Builder(1);
        if (selected) builder.WithRecordPath("$");
        using var reader = builder.Build().ToDataReader(Input(document));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
        Assert.That(exception!.Message, Does.Contain($"position {document.IndexOf("/* unfinished", StringComparison.Ordinal)}"));
    }

    private static JsonReaderBuilder Builder(int bufferSize, int projection = 0)
    {
        var builder = new JsonReaderBuilder().WithJsonc()
            .WithParserOptimizations(new(BufferSize: bufferSize, ReadAhead: false));
        if (projection == 1) builder.WithProjection(p => p.Property("id").Property("text").Property("items"));
        if (projection == 2) builder.WithOrderedProjection(p => p.Property("id").Property("text").Property("items"));
        return builder;
    }

    private static MemoryStream Input(string content) => new(Encoding.UTF8.GetBytes(content));

    private sealed class AsyncOnlyWhenRequestedStream(Stream inner, bool asyncOnly) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O is not allowed.");
            var read = inner.Read(buffer, offset, Math.Min(count, 64));
            BytesRead += read;
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O is not allowed.");
            var read = inner.Read(buffer[..Math.Min(buffer.Length, 64)]);
            BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, 64)], cancellationToken);
            BytesRead += read;
            return read;
        }
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
