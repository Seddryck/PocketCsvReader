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
        public bool IsEndOfFile(out RecordSpan record, out RecordState state) => throw new InvalidOperationException("Sync source read");
        public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return ReadCalls++ == rows
                ? new(true, RecordMemory.Empty, RecordState.Eof)
                : new(false, _record, RecordState.Record);
        }
        public void Dispose() { }
    }
}
