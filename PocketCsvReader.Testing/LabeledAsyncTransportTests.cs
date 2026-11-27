using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class LabeledAsyncTransportTests
{
    [Test]
    public async Task SuspendingPublicSourcesDoNotAcquireAForwardingAllocation()
    {
        const int rows = 3000;
        async Task<long> Measure(bool reference, int count)
        {
            using ProbeReader reader = reference ? new DirectReader(count) : new ProbeReader(count);
            long before = GC.GetTotalAllocatedBytes(precise: true);
            int sum = 0;
            while (await reader.ReadAsync()) sum += reader.GetInt32(0);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            Assert.That(sum, Is.EqualTo(count * 7));
            Assert.That(reader.Source.ReadCalls, Is.EqualTo(count + 1));
            return allocated;
        }
        await Measure(true, 200);
        await Measure(false, 200);
        long direct = long.MaxValue, labeled = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            direct = Math.Min(direct, await Measure(true, rows));
            labeled = Math.Min(labeled, await Measure(false, rows));
        }
        // Compare with a direct public-source await on the same input/transport.
        // Allows setup, scheduling noise and 64 bytes/row; the former forwarding
        // state machine added substantially more whenever the source suspended.
        Assert.That(labeled, Is.LessThanOrEqualTo(direct + (rows * 64L) + 65536),
            $"direct={direct}, labeled={labeled}");
    }

    [Test]
    public async Task MixedReadsPreservePublicSourceEntryPoints()
    {
        using var reader = new ProbeReader(3);
        Assert.That(reader.Read(), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(reader.Source.SyncCalls, Is.EqualTo(2));
        Assert.That(reader.Source.ReadCalls, Is.EqualTo(4));
    }

    [Test]
    public async Task CharacterSourceUsesTheSynchronousViewAndKeepsTheAsyncPath()
    {
        using var reader = new CharacterReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(7));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(8));
        Assert.That(reader.Read(), Is.False);
    }

    private sealed class CharacterReader() : LabeledDataReader<CsvProfile>(
        new MemoryStream(), new CsvProfile(',', "\n")
        {
            StreamInitialization = StreamInitializationOptions.ForwardOnly,
        }, new StringMapper())
    {
        protected override IRecordSource<CsvProfile> CreateRecordSource(StreamReader reader, CsvProfile profile)
            => new CharacterSource(profile);
    }

    private sealed class CharacterSource(CsvProfile profile) : IBorrowedCharacterRecordSource<CsvProfile>
    {
        private readonly FieldSpan[] _fields = [new(new SpanInfo(2, 1), new SpanInfo(0, 2))];
        private int _rows;
        public CsvProfile Profile => profile;

        public bool ReadCharacters(out ReadOnlyMemory<char> characters, out FieldSpan[] fields, out RecordState state)
        {
            fields = _fields;
            if (_rows++ == 0)
            {
                characters = "id7".AsMemory();
                state = RecordState.Record;
                return false;
            }

            characters = default;
            state = RecordState.Eof;
            return true;
        }

        public ValueTask<CurrentRecordResult> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            Assert.That(_rows++, Is.EqualTo(1));
            return ValueTask.FromResult(new CurrentRecordResult(false, new CurrentRecord("id8".AsMemory(), _fields), RecordState.Record));
        }

        public bool IsEndOfFile(out RecordSpan record, out RecordState state)
            => throw new InvalidOperationException("The labeled reader must use the internal character view.");

        CurrentRecordResult ICurrentRecordSource.ReadCurrent()
            => throw new InvalidOperationException("The synchronous reader must use the smaller transport.");

        public void Dispose() { }
    }

    private class ProbeReader : LabeledDataReader<CsvProfile>
    {
        public SuspendingSource Source { get; }
        public ProbeReader(int rows)
            : base(new MemoryStream(), new CsvProfile(',', "\n")
            {
                StreamInitialization = StreamInitializationOptions.ForwardOnly
            }, new StringMapper())
            => Source = new(Profile, rows);

        protected override IRecordSource<CsvProfile> CreateRecordSource(StreamReader reader, CsvProfile profile) => Source;
    }

    // Reference implementation of the direct public-source await, deliberately
    // without an adapter task or another asynchronous method between the two.
    private sealed class DirectReader(int rows) : ProbeReader(rows)
    {
        private readonly string[] _labels = ["id"];
        protected override async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
        {
            var result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
            IsEof = result.IsEndOfFile;
            Record = result.Record;
            if (result.State == RecordState.Eof) return false;
            Fields = _labels;
            RowCount++;
            return true;
        }
    }

    private sealed class SuspendingSource(CsvProfile profile, int rows) : IRecordSource<CsvProfile>
    {
        private readonly RecordMemory _record = new("id7".AsSpan(),
            [new FieldSpan(new SpanInfo(2, 1), new SpanInfo(0, 2))]);
        public CsvProfile Profile => profile;
        public int ReadCalls { get; private set; }
        public int SyncCalls { get; private set; }
        public bool IsEndOfFile(out RecordSpan record, out RecordState state)
        {
            SyncCalls++;
            var result = Next();
            record = RecordSpan.FromMemory(result.Record.Span, result.Record.FieldSpans);
            state = result.State;
            return result.IsEndOfFile;
        }
        public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return Next();
        }

        private RecordReadResult Next() => ReadCalls++ == rows
            ? new(true, RecordMemory.Empty, RecordState.Eof)
            : new(false, _record, RecordState.Record);
        public void Dispose() { }
    }
}
