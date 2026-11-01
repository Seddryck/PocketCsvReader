using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.WebLogs.Testing;

public class WebLogBufferingTests
{
    private const string Header = "#Fields: host identity user timestamp request status bytes";
    private const string CommonTimestamp = "10/Oct/2000:13:55:36 -0700";

    [Test]
    [Combinatorial]
    public async Task RecordsAcrossBuffers_PreserveValuesAndEof(
        [Values(WebLogFormat.Common, WebLogFormat.W3cExtended)] WebLogFormat format,
        [Values(1, 7, 64, 4096)] int bufferSize,
        [Values("\n", "\r\n", "\r")] string newline,
        [Values] bool asynchronous)
    {
        var firstRequest = "/café/雪/" + new string('x', 9000);
        var input = Prefix(format, newline) + Line(format, firstRequest) + newline +
            Line(format, "/second", host: "192.0.2.12", status: "404", bytes: "-");
        using var reader = CreateReader(format, bufferSize).ToDataReader(Text(input));

        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(7));
            Assert.That(reader.GetString(0), Is.EqualTo("127.0.0.1"));
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.GetString(2), Is.EqualTo("frank"));
            Assert.That(reader.GetString(4), Is.EqualTo(firstRequest));
            Assert.That(reader.GetRawString(4), Is.EqualTo(firstRequest));
            Assert.That(reader.GetInt32(5), Is.EqualTo(200));
            Assert.That(reader.GetInt64(6), Is.EqualTo(4294967296L));
        });

        var saved = reader.GetString(4);
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("192.0.2.12"));
            Assert.That(reader.GetString(4), Is.EqualTo("/second"));
            Assert.That(reader.GetInt32(5), Is.EqualTo(404));
            Assert.That(reader.IsDBNull(6), Is.True);
            Assert.That(saved, Is.EqualTo(firstRequest));
        });
        Assert.That(await Read(reader, asynchronous), Is.False);
        Assert.That(await Read(reader, asynchronous), Is.False);
    }

    [TestCase(WebLogFormat.Common)]
    [TestCase(WebLogFormat.W3cExtended)]
    public void Source_RecordsInOneBuffer_ShareCharacterStorageAndFieldArray(WebLogFormat format)
    {
        var input = Prefix(format, "\n") + Line(format, "/one") + "\n" + Line(format, "/two") + "\n";
        using var textReader = new StreamReader(Text(input));
        using var source = new WebLogRecordSource(textReader, new WebLogProfile(format,
            parserOptimizations: new(BufferSize: 4096)));

        source.IsEndOfFile(out var first, out _);
        var fields = first.FieldSpans;
        Assert.That(MemoryMarshal.TryGetArray(first.AsMemory().Span, out var firstStorage), Is.True);
        Assert.That(first.Slice(4).ToString(), Is.EqualTo("/one"));

        source.IsEndOfFile(out var second, out _);
        Assert.That(MemoryMarshal.TryGetArray(second.AsMemory().Span, out var secondStorage), Is.True);
        var secondFields = second.FieldSpans;
        var secondValue = second.Slice(4).ToString();
        Assert.Multiple(() =>
        {
            Assert.That(secondFields, Is.SameAs(fields));
            Assert.That(secondStorage.Array, Is.SameAs(firstStorage.Array));
            Assert.That(secondStorage.Offset, Is.GreaterThan(firstStorage.Offset));
            Assert.That(secondValue, Is.EqualTo("/two"));
        });
    }

    [TestCase(WebLogFormat.Common)]
    [TestCase(WebLogFormat.W3cExtended)]
    public void RawParsing_StaysWithinWarmedAllocationBudget(WebLogFormat format)
    {
        const int rows = 10000;
        var line = Line(format, "/" + new string('x', 512)) + "\n";
        var data = Encoding.UTF8.GetBytes(Prefix(format, "\n") + string.Concat(Enumerable.Repeat(line, rows)));
        ReadAll(); // Warm JIT and shared pools; input generation is also outside measurement.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(count, Is.EqualTo(rows));
        // 64 bytes/row allows reader/schema setup and occasional cross-buffer segment bookkeeping.
        // A ~570-character line string/copy or a fresh seven-field scratch array exceeds this budget.
        Assert.That(allocated, Is.LessThan(64L * rows), $"Allocated {allocated} bytes for {rows} raw {format} records.");

        int ReadAll()
        {
            using var text = new StreamReader(new MemoryStream(data));
            using var source = new WebLogRecordSource(text, new WebLogProfile(format));
            var count = 0;
            while (!source.IsEndOfFile(out _, out _)) count++;
            return count;
        }
    }

    [Test]
    [Combinatorial]
    public async Task AsyncOnlyForwardStream_UsesAsyncIoAndStreamsBeforeEof(
        [Values(WebLogFormat.Common, WebLogFormat.W3cExtended)] WebLogFormat format, [Values] bool trackPosition)
    {
        var input = Prefix(format, "\r\n") + string.Concat(Enumerable.Repeat(Line(format, "/path") + "\r\n", 10000));
        var stream = new ForwardStream(input, asyncOnly: true);
        await using (var reader = CreateReader(format, 31, trackPosition).ToDataReader(stream))
        {
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(4), Is.EqualTo("/path"));
            Assert.That(stream.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(input)));
            Assert.That(stream.AsyncReads, Is.GreaterThan(0));
            Assert.That(stream.Disposed, Is.False);
        }
        Assert.That(stream.Disposed, Is.True);
    }

    [TestCase(WebLogFormat.Common)]
    [TestCase(WebLogFormat.W3cExtended)]
    public void SyncForwardStream_StreamsBeforeEofAndDisposesEarly(WebLogFormat format)
    {
        var input = Prefix(format, "\n") + string.Concat(Enumerable.Repeat(Line(format, "/path") + "\n", 10000));
        var stream = new ForwardStream(input);
        using (var reader = CreateReader(format, 31).ToDataReader(stream))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(4), Is.EqualTo("/path"));
            Assert.That(stream.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(input)));
        }
        Assert.That(stream.Disposed, Is.True);
    }

    [Test]
    [Combinatorial]
    public async Task Positions_SkipDirectivesAndBlankLinesAndIncludeTerminators(
        [Values(WebLogFormat.Common, WebLogFormat.W3cExtended)] WebLogFormat format, [Values] bool asynchronous)
    {
        var prefix = " \r\n" + Prefix(format, "\r\n");
        var first = Line(format, "/first") + "\r\n";
        var between = format == WebLogFormat.Common ? "\n" : "\n" + Header + "\r\n";
        var input = prefix + first + between + Line(format, "/last");
        using var reader = CreateReader(format, 1, trackPosition: true).ToDataReader(Text(input));

        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.CurrentPosition!.Value.RecordNumber, Is.EqualTo(1));
            Assert.That(reader.CurrentPosition.Value.Offset, Is.EqualTo(prefix.Length));
            Assert.That(reader.CurrentPosition.Value.PhysicalLine, Is.EqualTo(format == WebLogFormat.Common ? 2 : 3));
        });
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(prefix.Length + first.Length + between.Length));
        Assert.That(reader.CurrentPosition.Value.RecordNumber, Is.EqualTo(2));
        Assert.That(await Read(reader, asynchronous), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [Test]
    [Combinatorial]
    public async Task EmptyOrBlankInput_HasNoRecords(
        [Values(WebLogFormat.Common, WebLogFormat.W3cExtended)] WebLogFormat format, [Values("", " \r\n\t\n\r")] string input, [Values] bool asynchronous)
    {
        using var reader = CreateReader(format, 1).ToDataReader(Text(input));
        Assert.That(await Read(reader, asynchronous), Is.False);
    }

    [Test]
    [Combinatorial]
    public async Task MalformedRecord_PreservesLineNumberAndDiagnostic(
        [Values(WebLogFormat.Common, WebLogFormat.W3cExtended)] WebLogFormat format, [Values] bool asynchronous)
    {
        var broken = format == WebLogFormat.Common
            ? "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"unfinished"
            : "one two three four five six seven eight nine";
        var input = "\n" + Prefix(format, "\n") + broken;
        using var reader = CreateReader(format, 3).ToDataReader(Text(input));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => { await Read(reader, asynchronous); });
        Assert.That(exception!.Message, Does.Contain(format == WebLogFormat.Common ? "line 2" : "line 3"));
        Assert.That(exception.Message, Does.Contain(format == WebLogFormat.Common ? "closing" : "expected 7 fields but found 9"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task W3cDirectives_AreRetainedAcrossRefillsAndSchemaIsStable(bool asynchronous)
    {
        var input = "#Software: café-雪\r\n#ignored\r\n#Fields: host request\r\n127.0.0.1 /first\r\n" +
            "#Fields: host request\n192.0.2.1 /last\n#Fields: request host\n";
        using var reader = CreateReader(WebLogFormat.W3cExtended, 2).ToDataReader(Text(input));
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.Directives[0].Value, Is.EqualTo("café-雪"));
            Assert.That(reader.GetName(1), Is.EqualTo("request"));
            Assert.That(reader.GetString(1), Is.EqualTo("/last"));
            Assert.That(reader.Directives.Count, Is.EqualTo(3));
        });
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => { await Read(reader, asynchronous); });
        Assert.That(exception!.Message, Does.Contain("changes the schema"));
        Assert.That(exception.Message, Does.Contain("line 7"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClfQuotedRequests_PreserveSpacesAndEscapedQuoteSyntax(bool asynchronous)
    {
        const string request = "GET /a\\\"b HTTP/1.1";
        using var reader = CreateReader(WebLogFormat.Common, 1).ToDataReader(Text(Line(WebLogFormat.Common, request)));
        Assert.That(await Read(reader, asynchronous), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo(request));
    }

    [Test]
    public void TypedRecords_RemainOwnedAfterLaterReadsAndDisposal()
    {
        var request = "/" + new string('x', 9000);
        var entries = ((CommonLogReader)CreateReader(WebLogFormat.Common, 7))
            .ToCommonLog(Text(Line(WebLogFormat.Common, request) + "\n" + Line(WebLogFormat.Common, "/last"))).ToList();
        Assert.That(entries[0].Request, Is.EqualTo(request));
        Assert.That(entries[1].Request, Is.EqualTo("/last"));
        Assert.That(entries[0].Timestamp!.Value.Offset, Is.EqualTo(TimeSpan.FromHours(-7)));
    }

    [TestCase(WebLogFormat.Common)]
    [TestCase(WebLogFormat.W3cExtended)]
    public async Task AlternatingSyncAndAsyncReads_UseOneCursor(WebLogFormat format)
    {
        var input = Prefix(format, "\r\n") + Line(format, "/first") + "\r\n" +
            Line(format, "/second") + "\r" + Line(format, "/third");
        using var reader = CreateReader(format, 7).ToDataReader(Text(input));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo("/first"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo("/second"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo("/third"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase("#Fields: \n", "does not define any fields")]
    [TestCase("one two three\n", "required #Fields")]
    public async Task W3cSchemaErrors_ArePreservedOnAsyncPath(string input, string diagnostic)
    {
        using var reader = CreateReader(WebLogFormat.W3cExtended, 1).ToDataReader(Text(input));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => { await reader.ReadAsync(); });
        Assert.That(exception!.Message, Does.Contain(diagnostic));
        Assert.That(exception.Message, Does.Contain("line 1"));
    }

    private static string Prefix(WebLogFormat format, string newline)
        => format == WebLogFormat.Common ? "" : Header + newline;

    private static string Line(WebLogFormat format, string request, string host = "127.0.0.1", string status = "200", string bytes = "4294967296")
        => format == WebLogFormat.Common
            ? $"{host} - frank [{CommonTimestamp}] \"{request}\" {status} {bytes}"
            : $"{host} - frank 2000-10-10T13:55:36-07:00 {request} {status} {bytes}";

    private static WebLogReader CreateReader(WebLogFormat format, int bufferSize, bool trackPosition = false)
    {
        var profile = new WebLogProfile(format, parserOptimizations: new(BufferSize: bufferSize) { TrackPosition = trackPosition });
        return format == WebLogFormat.Common ? new CommonLogReader(profile) : new W3cExtendedLogReader(profile);
    }

    private static Task<bool> Read(WebLogDataReader reader, bool asynchronous)
        => asynchronous ? reader.ReadAsync() : Task.FromResult(reader.Read());

    private static MemoryStream Text(string input) => new(Encoding.UTF8.GetBytes(input));

    private sealed class ForwardStream(string input, bool asyncOnly = false) : Stream
    {
        private readonly MemoryStream _inner = Text(input);
        public long BytesRead { get; private set; }
        public int AsyncReads { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O is forbidden.");
            var read = _inner.Read(buffer, offset, Math.Min(count, 17));
            BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AsyncReads++;
            var read = await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 17)], cancellationToken);
            BytesRead += read;
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
