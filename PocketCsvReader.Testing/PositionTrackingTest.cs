using System.IO.Compression;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class PositionTrackingTest
{
    private static CsvProfile Profile(CsvInputMode mode, bool tracking = true, int size = 3, bool ahead = false,
        DialectDescriptor? dialect = null, ResourceDescriptor? resource = null)
        => new(dialect ?? new(Header: false, LineTerminator: "\r\n"), resource: resource)
        {
            StreamInitialization = new(InputMode: mode),
            ParserOptimizations = new(BufferSize: size, ReadAhead: ahead) { TrackPosition = tracking }
        };

    private static IEnumerable<TestCaseData> Modes()
    {
        foreach (var mode in new[] { CsvInputMode.DecodedChars, CsvInputMode.Utf8Bytes, CsvInputMode.Auto })
        foreach (bool async in new[] { false, true })
        foreach (int size in new[] { 1, 3, 64 })
        foreach (bool ahead in new[] { false, true })
            yield return new TestCaseData(mode, async, size, ahead);
    }

    private static long Offset(string prefix, CsvInputMode mode)
        => mode == CsvInputMode.DecodedChars ? prefix.Length : Encoding.UTF8.GetByteCount(prefix);

    [TestCaseSource(nameof(Modes))]
    public async Task HeadersCommentsUnicodeMultilineAndEof(CsvInputMode mode, bool async, int size, bool ahead)
    {
        const string prefix = "name,value\r\n# ignored 😀\r\n";
        const string first = "\"é\r\n東京😀\",1\r\n";
        const string last = "last,2";
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + first + last)),
            Profile(mode, size: size, ahead: ahead, dialect: new(Header: true, CommentChar: '#', LineTerminator: "\r\n")));
        Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        var unit = mode == CsvInputMode.DecodedChars ? PositionOffsetUnit.Utf16CodeUnits : PositionOffsetUnit.Bytes;
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(1, 3, Offset(prefix, mode), unit)));
        var saved = reader.CurrentPosition;
        Assert.That(reader.GetString(0), Is.EqualTo("é\r\n東京😀"));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 5, Offset(prefix + first, mode), unit)));
        Assert.That(saved!.Value.RecordNumber, Is.EqualTo(1));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public async Task DisabledNeverExposesPosition(CsvInputMode mode, bool async)
    {
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("é,1\r\na,2")), Profile(mode, false));
        while (async ? await reader.ReadAsync() : reader.Read()) Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(new ParserOptimizationOptions().TrackPosition, Is.False);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void CustomTerminatorCountsPhysicalNewlinesInsideBulkPayload(CsvInputMode mode)
    {
        const string first = "\"a\r\nb\nc\rd😀\",1||";
        foreach (int size in new[] { 1, 2, 7, 4096 })
        {
            using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(first + "next,2||")),
                Profile(mode, size: size, dialect: new(Header: false, LineTerminator: "||")));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(1));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(4));
            Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(Offset(first, mode)));
            Assert.That(reader.Read(), Is.False);
            Assert.That(reader.CurrentPosition, Is.Null);
        }
    }

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public async Task BomNonSeekableAndCompressedInput(CsvInputMode mode, bool async)
    {
        foreach (bool bom in new[] { false, true })
        foreach (bool compressed in new[] { false, true })
        {
            byte[] bytes = [.. bom ? Encoding.UTF8.Preamble.ToArray() : [], .. Encoding.UTF8.GetBytes("é,1\r\n😀,2")];
            if (compressed)
            {
                using var output = new MemoryStream();
                using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(bytes);
                bytes = output.ToArray();
            }
            var profile = Profile(mode, resource: compressed ? new(Compression: "gzip", Encoding: "utf-8") : null);
            using var reader = new CsvDataReader(new ForwardStream(bytes, async), profile);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.CurrentPosition!.Value.Offset, Is.Zero);
            Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(Offset("é,1\r\n", mode)));
        }
    }

    [TestCase("utf-16")]
    [TestCase("utf-16BE")]
    [TestCase("utf-32")]
    public void OtherEncodingsAndFallbackUseCharacterUnits(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = [.. encoding.Preamble, .. encoding.GetBytes("😀,1\r\né,2")];
        using var reader = new CsvDataReader(new MemoryStream(bytes), Profile(CsvInputMode.Auto, resource: new(Encoding: encodingName)));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 2, "😀,1\r\n".Length, PositionOffsetUnit.Utf16CodeUnits)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SkippedMalformedRecordAndDiagnosticsUseOneTracker(bool async)
    {
        const string prefix = "h,v\r\n#skip\r\n";
        const string bad = "\"bad\"x,z\r\n";
        BadDataContext? diagnostic = null;
        var profile = Profile(CsvInputMode.Auto, size: 2,
            dialect: new(Header: true, CommentChar: '#', LineTerminator: "\r\n"));
        profile.BadDataPolicy = new(context => { diagnostic = context; return BadDataAction.SkipRecord; });
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + bad + "ok,2")), profile);
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(2, 4, prefix.Length + bad.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(diagnostic!.RecordPosition, Is.EqualTo(new RecordPosition(1, 3, prefix.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(diagnostic.Offset, Is.EqualTo(prefix.Length + 5));
        Assert.That(diagnostic.OffsetUnit, Is.EqualTo(PositionOffsetUnit.Utf16CodeUnits));
        Assert.That(diagnostic.PhysicalLine, Is.EqualTo(3));
    }

    [Test]
    public void BuilderActivationAndExplicitCommentRows()
    {
        var csv = new CsvReaderBuilder().WithPositionTracking()
            .WithParserOptimizations(new(ReadAhead: false))
            .WithDialect(d => d.WithoutHeader().WithLineTerminator("\r\n")).Build();
        using var reader = csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a,1\r\nb,2")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.Not.Null);
        var profile = Profile(CsvInputMode.Utf8Bytes, dialect: new(Header: false, CommentRows: [1], LineTerminator: "\r\n"));
        using var second = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("skip,1\r\nok,2")), profile);
        Assert.That(second.Read(), Is.True);
        Assert.That(second.CurrentPosition!.Value.RecordNumber, Is.EqualTo(1));
        Assert.That(second.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(2));
    }

    [Test]
    public void PositionAccessDoesNotDecodeUtf8Record()
    {
        using var reader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes("東京😀,é\r\n")), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.OffsetUnit, Is.EqualTo(PositionOffsetUnit.Bytes));
        Assert.That(typeof(RecordMemory).GetField("_characters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader.CurrentRecord), Is.Null);
        Assert.That(typeof(RecordPosition).IsValueType, Is.True);
        reader.Close();
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void TrackingAddsOnlyFixedReaderAllocations(CsvInputMode mode)
    {
        long Measure(int rows, bool tracking)
        {
            var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("a,b\r\n", rows)));
            var profile = Profile(mode, tracking, size: 4096);
            using var stream = new MemoryStream(data);
            long before = GC.GetAllocatedBytesForCurrentThread();
            using (var reader = new CsvDataReader(stream, profile))
                while (reader.Read()) { var position = reader.CurrentPosition; }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        for (int i = 0; i < 3; i++) { Measure(20, false); Measure(20, true); }
        long smallDelta = Measure(20, true) - Measure(20, false);
        long largeDelta = Measure(2000, true) - Measure(2000, false);
        Assert.That(largeDelta, Is.EqualTo(smallDelta).Within(64), "Position metadata must not allocate per row.");
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void BlankRecordsAndDefaultBuilder(CsvInputMode mode)
    {
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("\r\né\r\n")), Profile(mode, size: 1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.RecordNumber, Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.RecordNumber, Is.EqualTo(2));
        Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(2));
        using var defaults = new CsvReaderBuilder().WithDialect(d => d.WithoutHeader()).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a\r\n")));
        Assert.That(defaults.Read(), Is.True);
        Assert.That(defaults.CurrentPosition, Is.Null);
    }

    [Test]
    public void AdapterHidesPrefetchedPositionUntilRead()
    {
        using var adapter = new CsvReaderBuilder().WithPositionTracking()
            .WithDialect(d => d.WithoutHeader()).Build()
            .ToDbDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a\r\n")));
        var reader = (DbDataReaderAdapter)adapter;
        Assert.That(reader.HasRows, Is.True);
        Assert.That(reader.CurrentPosition, Is.Null);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.RecordNumber, Is.EqualTo(1));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [Test]
    public void MalformedMultilineDiagnosticsDistinguishRecordStartFromOffendingInput()
    {
        const string prefix = "h,v\r\nok,1\r\n";
        const string bad = "\"a\r\nb\"x,z\r\n";
        BadDataContext? diagnostic = null;
        var profile = Profile(CsvInputMode.Auto, size: 1, dialect: new(Header: true, LineTerminator: "\r\n"));
        profile.BadDataPolicy = new(context => { diagnostic = context; return BadDataAction.SkipRecord; });
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(prefix + bad + "last,2")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(diagnostic!.RecordPosition, Is.EqualTo(new RecordPosition(2, 3, prefix.Length, PositionOffsetUnit.Utf16CodeUnits)));
        Assert.That(diagnostic.RecordNumber, Is.EqualTo(2));
        Assert.That(diagnostic.PhysicalLine, Is.EqualTo(4));
        Assert.That(diagnostic.Offset, Is.EqualTo(prefix.Length + 6));
        Assert.That(reader.CurrentPosition, Is.EqualTo(new RecordPosition(3, 5, prefix.Length + bad.Length, PositionOffsetUnit.Utf16CodeUnits)));
    }

    [Test]
    public void ExplicitBuilderDisableOverridesOptionsAndUnsupportedUtf8StillRejects()
    {
        using var disabled = new CsvReaderBuilder().WithPositionTracking(false)
            .WithParserOptimizations(new() { TrackPosition = true })
            .WithDialect(d => d.WithoutHeader()).Build()
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a\r\n")));
        Assert.That(disabled.Read(), Is.True);
        Assert.That(disabled.CurrentPosition, Is.Null);
        var profile = Profile(CsvInputMode.Utf8Bytes);
        profile.BadDataPolicy = new(_ => BadDataAction.SkipRecord);
        using var forced = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a\r\n")), profile);
        Assert.That(() => forced.Read(), Throws.TypeOf<NotSupportedException>());
    }
    [TestCase(false)] [TestCase(true)]
    public async Task DisposingClearsCurrentPosition(bool async)
    {
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("a\r\n")), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition, Is.Not.Null);
        if (async) await reader.DisposeAsync(); else reader.Dispose();
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    private sealed class InspectingReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile)
    { public RecordMemory CurrentRecord => Record!; }

    private sealed class ForwardStream(byte[] bytes, bool asyncOnly) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => asyncOnly ? throw new InvalidOperationException("Synchronous read") : _inner.Read(buffer, offset, Math.Min(count, 2));
        public override int Read(Span<byte> buffer) => asyncOnly ? throw new InvalidOperationException("Synchronous read") : _inner.Read(buffer[..Math.Min(buffer.Length, 2)]);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_inner.Read(buffer.Span[..Math.Min(buffer.Length, 2)]));
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
