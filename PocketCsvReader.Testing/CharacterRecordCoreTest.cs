using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class CharacterRecordCoreTest
{
    private static CsvProfile Profile() => new(new(Header: false));

    [TestCase(false)]
    [TestCase(true)]
    public async Task SingleBufferRecordBorrowsInputMemory(bool asynchronous)
    {
        var chunks = new Chunks("é,東京\r\n", 32, asynchronous);
        var pool = new TrackingPool();
        using var parser = new BufferedParser(chunks, Profile(), pool);
        var result = asynchronous ? await parser.ReadAsync() : ReadSync(parser);
        Assert.That(MemoryMarshal.TryGetArray(result.Record.Span, out var storage), Is.True);
        Assert.That(storage.Array, Is.SameAs(chunks.Storage));
        Assert.That(pool.Rented, Is.Empty);
        Assert.That(result.Record.Slice(1).ToString(), Is.EqualTo("東京"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FirstRecordReturnsBeforeTheRemainingDocumentIsConsumed(bool asynchronous)
    {
        var chunks = new Chunks("first,1\r\n" + new string('x', 100000), 32, asynchronous);
        var pool = new TrackingPool();
        using var parser = new BufferedParser(chunks, Profile(), pool);
        var result = asynchronous ? await parser.ReadAsync() : ReadSync(parser);
        Assert.That(result.Record.Slice(0).ToString(), Is.EqualTo("first"));
        Assert.That(chunks.IsEof, Is.False);
        Assert.That(pool.Rented, Is.Empty);
    }

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(1024)]
    public async Task MixedReadsKeepLongRecordsAndEofMemoryIntact(int chunkSize)
    {
        var value = string.Concat(Enumerable.Repeat("é東京😀", 1000));
        var text = value + ",first\r\nshort,second\r\n" + value + ",last";
        var pool = new TrackingPool();
        using (var parser = new BufferedParser(new Chunks(text, chunkSize), Profile(), pool))
        {
            var first = ReadSync(parser);
            Assert.That(first.Record.Span.ToString(), Is.EqualTo(value + ",first\r\n"));
            Assert.That(first.Record.Slice(0).ToString(), Is.EqualTo(value));
            var second = await parser.ReadAsync();
            Assert.That(second.Record.Slice(1).ToString(), Is.EqualTo("second"));
            var last = ReadSync(parser);
            Assert.That(last.IsEndOfFile, Is.True);
            Assert.That(last.Record.Span.ToString(), Is.EqualTo(value + ",last"));
            Assert.That(last.Record.Slice(0).ToString(), Is.EqualTo(value));
            var exhausted = await parser.ReadAsync();
            Assert.That(exhausted.IsEndOfFile, Is.True);
            Assert.That(exhausted.Record.FieldSpans, Is.Empty);
        }
        Assert.That(pool.Returned.Count, Is.EqualTo(pool.Rented.Count));
        Assert.That(pool.Returned.Distinct().Count(), Is.EqualTo(pool.Returned.Count));
        Assert.That(pool.Returned, Is.EquivalentTo(pool.Rented));
    }

    [TestCase(1)]
    [TestCase(4)]
    [TestCase(4096)]
    public async Task BadDataActionsShareRowsAndDiagnosticsAcrossIoModes(int chunkSize)
    {
        foreach (var action in new[] { BadDataAction.SkipRecord, BadDataAction.ReturnPartialRecord, BadDataAction.Stop })
        {
            var sync = await ReadBadRows(action, chunkSize, false);
            var asynchronous = await ReadBadRows(action, chunkSize, true);
            Assert.That(asynchronous.Rows, Is.EqualTo(sync.Rows));
            Assert.That(asynchronous.Diagnostics, Is.EqualTo(sync.Diagnostics));
            Assert.That(sync.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(sync.Rows.Count, Is.EqualTo(action == BadDataAction.Stop ? 1 : action == BadDataAction.SkipRecord ? 2 : 3));
        }
    }

    private static async Task<(List<string[]> Rows, List<string> Diagnostics)> ReadBadRows(BadDataAction action, int chunkSize, bool asynchronous)
    {
        var diagnostics = new List<string>();
        var profile = Profile();
        profile.BadDataPolicy = new(context =>
        {
            diagnostics.Add($"{context.RecordNumber}:{context.FieldIndex}:{context.Offset}:{context.OffendingInput}");
            return action;
        });
        using var parser = new BufferedParser(new Chunks("ok,1\r\n\"bad\"x,z\r\nnext,2", chunkSize, asynchronous), profile, null);
        var rows = new List<string[]>();
        RecordReadResult result;
        do
        {
            result = asynchronous ? await parser.ReadAsync() : ReadSync(parser);
            if (result.State == RecordState.Record)
                rows.Add(Enumerable.Range(0, result.Record.FieldSpans.Length).Select(i => result.Record.Slice(i).ToString()).ToArray());
        } while (!result.IsEndOfFile);
        return (rows, diagnostics);
    }

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public async Task SharedHeadersSelectAndJoinConfiguredRows(CsvInputMode mode, bool asynchronous)
    {
        var profile = new CsvProfile(new(HeaderRows: [1, 3]))
        {
            StreamInitialization = new(ProbeEncoding: false, InputMode: mode),
            ParserOptimizations = new(BufferSize: 3, ReadAhead: false)
        };
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("A,B\r\nskip,skip\r\nC,D\r\n1,2\r\n")), profile);
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "A C", "B D" }));
        Assert.That(reader.GetString(1), Is.EqualTo("2"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SharedHeaderDispatchPreservesVirtualOverrides(bool asynchronous)
    {
        using var reader = new HeaderReader(new MemoryStream(Encoding.UTF8.GetBytes("original\r\nvalue\r\n")), new CsvProfile(new()));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("custom"));
        Assert.That(reader.GetString(0), Is.EqualTo("value"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InitializationConsumesOnlyTheBomAndPreservesLeadingDataFeff(bool asynchronous)
    {
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("\uFEFFvalue,1\r\n")];
        foreach (var mode in new[] { CsvInputMode.DecodedChars, CsvInputMode.Auto, CsvInputMode.Utf8Bytes })
        foreach (bool explicitEncoding in new[] { false, true })
        {
            var profile = new CsvProfile(new(Header: false), resource: explicitEncoding ? new(Encoding: "utf-8") : null)
            {
                StreamInitialization = new(InputMode: mode)
            };
            using var reader = new CsvDataReader(new MemoryStream(bytes), profile);
            Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("\uFEFFvalue"), $"{mode}, explicit: {explicitEncoding}");
        }
    }

    private static RecordReadResult ReadSync(IRecordSource<CsvProfile> source)
    {
        bool eof = source.IsEndOfFile(out var record, out var state);
        return new(eof, state == RecordState.Record ? record.AsMemory() : RecordMemory.Empty, state);
    }

    private sealed class HeaderReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile)
    {
        protected override BaseRecordParser<CsvProfile> CreateRecordParser(StreamReader reader, CsvProfile profile)
            => new HeaderParser(reader, profile);
    }

    private sealed class HeaderParser(StreamReader reader, CsvProfile profile) : RecordParser(reader, profile)
    {
        public override string[][] ReadHeaders() { base.ReadHeaders(); return [["custom"]]; }
        public override async ValueTask<string[][]> ReadHeadersAsync(CancellationToken cancellationToken = default)
        {
            await base.ReadHeadersAsync(cancellationToken);
            return [["custom"]];
        }
    }

    private sealed class BufferedParser(IBufferReader reader, CsvProfile profile, ArrayPool<char>? pool)
        : RecordParser(profile, reader, pool);

    private sealed class Chunks(string text, int size, bool asyncOnly = false) : IBufferReader
    {
        public char[] Storage { get; } = new char[size];
        private int _position;
        public bool IsEof => _position == text.Length;
        public ReadOnlyMemory<char> Read()
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous refill was used.");
            return Fill();
        }
        public ValueTask<ReadOnlyMemory<char>> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fill());
        }
        private ReadOnlyMemory<char> Fill()
        {
            int length = Math.Min(size, text.Length - _position);
            text.AsSpan(_position, length).CopyTo(Storage);
            _position += length;
            return Storage.AsMemory(0, length);
        }
        public void Reset() => _position = 0;
        public void Dispose() { }
    }

    private sealed class TrackingPool : ArrayPool<char>
    {
        public List<char[]> Rented { get; } = new();
        public List<char[]> Returned { get; } = new();
        public override char[] Rent(int minimumLength)
        {
            var array = new char[minimumLength];
            Rented.Add(array);
            return array;
        }
        public override void Return(char[] array, bool clearArray = false) => Returned.Add(array);
    }
}
