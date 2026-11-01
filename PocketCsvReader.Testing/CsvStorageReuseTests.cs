using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CsvStorageReuseTests
{
    private static CsvProfile Profile(CsvInputMode mode, int bufferSize = 4096)
        => new(new(Header: false, LineTerminator: "\n"), resource: new ResourceDescriptor(Encoding: "utf-8"))
        {
            ParserOptimizations = new(BufferSize: bufferSize, ReadAhead: false),
            StreamInitialization = new(ProbeEncoding: false, InputMode: mode)
        };

    private static CsvDataReader Reader(string text, CsvInputMode mode, int bufferSize = 4096)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(text)), Profile(mode, bufferSize));

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public async Task Reader_ReusesMetadataWithoutMaterializingWrappers(CsvInputMode mode, bool asynchronous)
    {
        using var reader = Reader("first,200,1024\n東京😀,404,2048\nlast,201,4096", mode, 5);
        var fieldsProperty = typeof(BaseRawRecord<CsvProfile>).GetProperty("CurrentFieldSpans", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var wrapperField = typeof(BaseRawRecord<CsvProfile>).GetField("_record", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldSpan[]? fields = null;
        var retained = new List<string>();
        foreach (var expected in new[] { ("first", 200, 1024L), ("東京😀", 404, 2048L), ("last", 201, 4096L) })
        {
            Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
            var current = (FieldSpan[])fieldsProperty.GetValue(reader)!;
            if (fields is not null) Assert.That(current, Is.SameAs(fields));
            fields = current;
            retained.Add(reader.GetString(0));
            Assert.That(retained[^1], Is.EqualTo(expected.Item1));
            Assert.That(reader.GetInt32(1), Is.EqualTo(expected.Item2));
            Assert.That(reader.GetInt64(2), Is.EqualTo(expected.Item3));
            Assert.That(wrapperField.GetValue(reader), Is.Null, "Normal field access must not force the compatibility wrapper.");
        }
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);
        reader.Dispose();
        Assert.That(retained, Is.EqualTo(new[] { "first", "東京😀", "last" }));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void ChangingFieldCounts_DoNotExposeStaleMetadata(CsvInputMode mode)
    {
        using var reader = Reader("a,b,c\nx,y\nlast,two,three\n", mode, 3);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(3));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader.GetString(1), Is.EqualTo("y"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(3));
        Assert.That(reader.GetString(2), Is.EqualTo("three"));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void PublicSourceSync_PreservesOwnedMetadata(CsvInputMode mode)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,bb,ccc\nlonger,b,c\n"));
        using var source = CsvStreamSource.Create(stream, Profile(mode))!;
        var first = ReadFields(source);
        var snapshot = first.ToArray();
        var second = ReadFields(source);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(first, Is.EqualTo(snapshot));

        static FieldSpan[] ReadFields(IRecordSource<CsvProfile> source)
        {
            source.IsEndOfFile(out var record, out var state);
            Assert.That(state, Is.EqualTo(RecordState.Record));
            return record.Current.Fields!;
        }
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task PublicSourceAsync_PreservesOwnedMetadata(CsvInputMode mode)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,bb,ccc\nlonger,b,c\n"));
        using var source = CsvStreamSource.Create(stream, Profile(mode))!;
        var first = (await source.ReadAsync()).Record;
        var snapshot = first.ParsedFields.ToArray();
        var second = (await source.ReadAsync()).Record;
        Assert.That(second.ParsedFields, Is.Not.SameAs(first.ParsedFields));
        Assert.That(first.ParsedFields, Is.EqualTo(snapshot));
    }

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public void RawReaderAllocation_IsBoundedBySetupNotRowCount(CsvInputMode mode, bool asynchronous)
    {
        const int rows = 2000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("host,-,user,timestamp,GET /item HTTP/1.1,200,1024\n", rows)));
        int ReadAll()
        {
            using var reader = new CsvDataReader(new MemoryStream(data), Profile(mode));
            int count = 0;
            while (asynchronous ? reader.ReadAsync().GetAwaiter().GetResult() : reader.Read())
                count++;
            return count;
        }
        for (int i = 0; i < 3; i++) ReadAll();
        int thread = Environment.CurrentManagedThreadId;
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = ReadAll();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread), "MemoryStream completes without thread hops.");
        Assert.That(count, Is.EqualTo(rows));
        // Includes initialization, headers, pools and refills. A wrapper (80 B/row)
        // or seven-field snapshot (416 B/row) alone exceeds this 24 B/row budget.
        Assert.That(allocated, Is.LessThan(24L * rows), $"Allocated {allocated} bytes.");
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task AsyncTransport_UsesOnlyAsyncIO(CsvInputMode mode)
    {
        using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("\"one\n東京😀\",200,1024\n" + new string('x', 10000) + ",404,2048"));
        using var reader = new CsvDataReader(stream, Profile(mode, 3));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("one\n東京😀"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Has.Length.EqualTo(10000));
        Assert.That(reader.GetInt64(2), Is.EqualTo(2048));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task SourceCapability_IsResolvedOnceForMixedReads()
    {
        using var reader = new CountingReader(new MemoryStream(Encoding.UTF8.GetBytes("one,1\ntwo,2\nthree,3\n")));
        reader.Initialize();
        Assert.That(reader.Parser!.CapabilityChecks, Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("three"));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(reader.Parser.CapabilityChecks, Is.EqualTo(1));
    }

    private sealed class CountingReader(Stream stream) : CsvDataReader(stream,
        new CsvProfile(new(Header: false, LineTerminator: "\n"))
        { StreamInitialization = StreamInitializationOptions.ForwardOnly })
    {
        public CountingParser? Parser;
        protected override BaseRecordParser<CsvProfile> CreateRecordParser(StreamReader reader, CsvProfile profile)
            => Parser = new CountingParser(reader, profile);
    }

    private sealed class CountingParser(StreamReader reader, CsvProfile profile) : RecordParser(reader, profile)
    {
        public int CapabilityChecks;
        protected override bool SupportsBorrowedRecords { get { CapabilityChecks++; return true; } }
    }

    [Test]
    public async Task CustomParserOverrides_AreNotBypassed()
    {
        using var reader = new CustomReader(new MemoryStream(Encoding.UTF8.GetBytes("one,1\ntwo,2\n")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.Parser!.SyncReads, Is.EqualTo(1));
        Assert.That(reader.Parser.AsyncReads, Is.EqualTo(1));
        Assert.That(reader.GetString(0), Is.EqualTo("two"));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task MixedReads_UseTheSameBorrowedStorage(CsvInputMode mode)
    {
        using var reader = Reader("first,1\nsecond,2\nthird,3", mode, 3);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("first"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("second"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void LegacyRecordExposure_PreservesOwnedMetadata(CsvInputMode mode)
    {
        using var reader = new ExposingReader(new MemoryStream(Encoding.UTF8.GetBytes("first,1\nlonger,22\n")), Profile(mode));
        Assert.That(reader.Read(), Is.True);
        var retained = reader.Expose();
        var snapshot = retained.ParsedFields.ToArray();
        Assert.That(reader.Read(), Is.True);
        Assert.That(retained.ParsedFields, Is.EqualTo(snapshot));
        Assert.That(reader.Expose().ParsedFields, Is.Not.SameAs(retained.ParsedFields));
    }

    private sealed class ExposingReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile)
    {
        public RecordMemory Expose() => Record!;
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task PublicAsyncMapper_CanRetainMetadata(CsvInputMode mode)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("first,1\nlonger,22\n"));
        var retained = new List<FieldSpan[]>();
        await foreach (var fields in new CsvReader(Profile(mode)).ToAsync<FieldSpan[]>(stream,
            (text, fields) => (FieldSpan[])fields)) retained.Add(fields);
        Assert.That(retained, Has.Count.EqualTo(2));
        Assert.That(retained[0], Is.Not.SameAs(retained[1]));
        Assert.That(retained[0][0].Value.Length, Is.EqualTo(5));
        Assert.That(retained[1][0].Value.Length, Is.EqualTo(6));
    }

    private sealed class CustomReader(Stream stream) : CsvDataReader(stream,
        new CsvProfile(new(Header: false, LineTerminator: "\n"))
        { StreamInitialization = StreamInitializationOptions.ForwardOnly })
    {
        public CustomParser? Parser;
        protected override BaseRecordParser<CsvProfile> CreateRecordParser(StreamReader reader, CsvProfile profile)
            => Parser = new CustomParser(reader, profile);
    }

    private sealed class CustomParser(StreamReader reader, CsvProfile profile) : RecordParser(reader, profile)
    {
        public int SyncReads, AsyncReads;
        public override bool IsEndOfFile(out RecordSpan record, out RecordState state)
        { SyncReads++; return base.IsEndOfFile(out record, out state); }
        public override async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        { AsyncReads++; return await base.ReadAsync(cancellationToken); }
    }

    private sealed class AsyncOnlyStream(byte[] bytes) : MemoryStream()
    {
        private int _offset;
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous I/O");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous I/O");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, bytes.Length - _offset);
            bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }
    }
}
