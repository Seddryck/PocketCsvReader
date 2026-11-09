using Apache.Arrow;
using Apache.Arrow.Ipc;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.Testing;

public class ArrowStreamExtensionsTests
{
    [Test]
    public void SyncEnumerationPreservesEmptyBatchesAndDisposesOnEarlyExit()
    {
        var source = new Source();
        using var input = new MemoryStream();
        using (var batches = source.ToArrowBatches(input).GetEnumerator())
        {
            Assert.That(batches.MoveNext(), Is.True);
            using var batch = batches.Current;
            Assert.That(batch.Length, Is.Zero);
            Assert.That(source.Input, Is.SameAs(input));
        }

        Assert.That(source.Reader.Disposed, Is.True);
        foreach (var batch in source.ToArrowBatches("fixture"))
        {
            using (batch)
                Assert.That(batch.Length, Is.EqualTo(2));
        }

        Assert.That(source.Input, Is.EqualTo("fixture"));
        Assert.That(source.Reader.Disposed, Is.True);
        Assert.That(source.ToArrowBatches("empty"), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AsyncEnumerationAwaitsOpeningAndPreservesBatchOrder(bool fromFile)
    {
        var source = new Source();
        using var input = new MemoryStream();
        var lengths = new List<int>();
        await foreach (var batch in fromFile ? source.ToArrowBatchesAsync("fixture") : source.ToArrowBatchesAsync(input))
        {
            using (batch)
                lengths.Add(batch.Length);
        }

        Assert.That(source.AsyncOpened, Is.True);
        Assert.That(source.Input, Is.EqualTo(fromFile ? (object)"fixture" : input));
        Assert.That(lengths, Is.EqualTo(new[] { 0, 2 }));
        Assert.That(source.Reader.Disposed, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CancellationReachesOpenAndRead(bool duringOpen)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source { OpenCancellation = duringOpen ? cancellation : null };
        source.Reader.ReadCancellation = duringOpen ? null : cancellation;
        await using var batches = source.ToArrowBatchesAsync("fixture").WithCancellation(cancellation.Token).GetAsyncEnumerator();
        try
        {
            await batches.MoveNextAsync();
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException exception)
        {
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        Assert.That(source.Reader.Disposed, Is.EqualTo(!duringOpen));
    }

    private sealed class Source : IArrowStreamSource
    {
        public TestStream Reader { get; } = new();
        public object? Input { get; private set; }
        public bool AsyncOpened { get; private set; }
        public CancellationTokenSource? OpenCancellation { get; init; }

        public IArrowArrayStream ToArrowStream(string filename) => Open(filename);
        public IArrowArrayStream ToArrowStream(Stream stream) => Open(stream);
        public ValueTask<IArrowArrayStream> ToArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
            => OpenAsync(filename, cancellationToken);
        public ValueTask<IArrowArrayStream> ToArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
            => OpenAsync(stream, cancellationToken);

        private IArrowArrayStream Open(object input)
        {
            Input = input;
            return Reader;
        }

        private async ValueTask<IArrowArrayStream> OpenAsync(object input, CancellationToken cancellationToken)
        {
            await Task.Yield();
            OpenCancellation?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            AsyncOpened = true;
            return Open(input);
        }
    }

    private sealed class TestStream : IArrowArrayStream
    {
        private int _next;

        public Schema Schema { get; } = new Schema.Builder().Build();
        public bool Disposed { get; private set; }
        public CancellationTokenSource? ReadCancellation { get; set; }

        public async ValueTask<RecordBatch?> ReadNextRecordBatchAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            ReadCancellation?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return _next++ switch
            {
                0 => new RecordBatch(Schema, [], 0),
                1 => new RecordBatch(Schema, [], 2),
                _ => null,
            };
        }

        public void Dispose() => Disposed = true;
    }
}
