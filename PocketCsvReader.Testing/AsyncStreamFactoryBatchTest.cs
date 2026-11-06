using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class AsyncStreamFactoryBatchTest
{
    private static CsvReader CreateReader() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithHeaderRepeat(false).WithLineTerminator("\n"))
        .WithRowNumber()
        .Build();

    [Test]
    public async Task ReadAsync_AwaitsEachFactoryAndPreservesHeadersAndRowNumbers()
    {
        using var firstCancellation = new CancellationTokenSource();
        using var nextCancellation = new CancellationTokenSource();
        var first = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStream = new TrackingStream("Name\nAda\n");
        var emptyStream = new TrackingStream("");
        var nextStream = new TrackingStream("Grace\n");
        var calls = 0;
        await using var reader = CreateReader().ToDataReader(
        [
            token =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(firstCancellation.Token));
                return new(first.Task);
            },
            token =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(nextCancellation.Token));
                Assert.That(firstStream.AsyncDisposals, Is.EqualTo(1));
                return new(emptyStream);
            },
            token =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(nextCancellation.Token));
                Assert.That(emptyStream.AsyncDisposals, Is.EqualTo(1));
                return new(next.Task);
            }
        ]);

        Assert.Throws<NotSupportedException>(() => reader.Read());
        Assert.That(calls, Is.Zero);
        var read = reader.ReadAsync(firstCancellation.Token);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(read.IsCompleted, Is.False);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync());
        first.SetResult(firstStream);
        Assert.That(await read, Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader["Name"], Is.EqualTo("Ada"));
        Assert.That(calls, Is.EqualTo(1));

        read = reader.ReadAsync(nextCancellation.Token);
        Assert.That(calls, Is.EqualTo(3));
        Assert.That(read.IsCompleted, Is.False);
        next.SetResult(nextStream);
        Assert.That(await read, Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader["Name"], Is.EqualTo("Grace"));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(nextStream.AsyncDisposals, Is.EqualTo(1));
        Assert.That(firstStream.SyncDisposals + emptyStream.SyncDisposals + nextStream.SyncDisposals, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadAsync_CancelledDuringAcquisition_ClosesBatch(bool returnsStreamAfterCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var result = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new TrackingStream("1");
        var calls = 0;
        await using var reader = CsvBatchDataReader.FromAsyncStreamFactories(
        [
            token =>
            {
                calls++;
                Assert.That(token, Is.EqualTo(cancellation.Token));
                return new(result.Task);
            },
            _ => throw new AssertionException("Unused factory was called.")
        ], new CsvProfile(',', "\n"));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => reader.ReadAsync(cancelled.Token));
        Assert.That(calls, Is.Zero);
        Assert.That(reader.IsClosed, Is.False);

        var read = reader.ReadAsync(cancellation.Token);
        cancellation.Cancel();
        if (returnsStreamAfterCancellation)
            result.SetResult(stream);
        else
            result.SetCanceled(cancellation.Token);

        var error = await Assert.CatchAsync<OperationCanceledException>(() => read);
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(stream.AsyncDisposals, Is.EqualTo(returnsStreamAfterCancellation ? 1 : 0));
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task ReadAsync_FactoryFailsAfterTransition_PropagatesErrorAndClosesBatch()
    {
        var first = new TrackingStream("1");
        var error = new IOException("Cannot open the second input.");
        await using var reader = CsvBatchDataReader.FromAsyncStreamFactories(
        [
            _ => new(first),
            _ => ValueTask.FromException<Stream>(error),
            _ => throw new AssertionException("Unused factory was called.")
        ], new CsvProfile(',', "\n"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync()), Is.SameAs(error));
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(first.AsyncDisposals, Is.EqualTo(1));
    }

    [Test]
    public async Task ReadAsync_CancelledDuringRecordIO_DisposesInput()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new TrackingStream("1")
        {
            ReadOverride = token =>
            {
                started.TrySetResult(token);
                return new(pending.Task.WaitAsync(token));
            }
        };
        await using var reader = CsvBatchDataReader.FromAsyncStreamFactories(
            [_ => new(stream), _ => throw new AssertionException("Unused factory was called.")],
            new CsvProfile(',', "\n"));

        var read = reader.ReadAsync(cancellation.Token);
        Assert.That(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(cancellation.Token));
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => read);
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(stream.AsyncDisposals, Is.EqualTo(1));
    }

    [TestCase("null factory")]
    [TestCase("null stream")]
    [TestCase("unreadable stream")]
    [TestCase("read failure")]
    public async Task ReadAsync_InvalidInput_ClosesBatchAndDisposesOwnedStream(string input)
    {
        var stream = new TrackingStream("1")
        {
            Readable = input != "unreadable stream",
            FailOnRead = input == "read failure"
        };
        Func<CancellationToken, ValueTask<Stream>> open = input switch
        {
            "null factory" => null!,
            "null stream" => _ => new ValueTask<Stream>((Stream)null!),
            _ => _ => new(stream)
        };
        await using var reader = CsvBatchDataReader.FromAsyncStreamFactories([open], new CsvProfile(',', "\n"));

        if (input == "read failure")
            await Assert.ThrowsAsync<IOException>(() => reader.ReadAsync());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync());
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(stream.AsyncDisposals, Is.EqualTo(input is "unreadable stream" or "read failure" ? 1 : 0));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Dispose_OnlyDisposesAcquiredStreams(bool asyncDisposal)
    {
        var acquired = new TrackingStream("Name\nAda");
        var reader = CreateReader().ToDataReader(
            [_ => new(acquired), _ => throw new AssertionException("Unused factory was called.")]);
        Assert.That(await reader.ReadAsync(), Is.True);

        if (asyncDisposal)
            await reader.DisposeAsync();
        else
            reader.Close();
        await reader.DisposeAsync();
        reader.Dispose();
        Assert.That(acquired.AsyncDisposals, Is.EqualTo(asyncDisposal ? 1 : 0));
        Assert.That(acquired.SyncDisposals, Is.EqualTo(asyncDisposal ? 0 : 1));
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task Dispose_BeforeFirstReadDoesNotInvokeFactories()
    {
        var reader = CreateReader().ToDataReader(
            [_ => throw new AssertionException("Unused factory was called.")]);
        await reader.DisposeAsync();
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DbReader_HasRowsRequiresAnAsyncRead(bool hasRows)
    {
        var calls = 0;
        Func<CancellationToken, ValueTask<Stream>>[] openers = hasRows
            ? [_ => { calls++; return new(new TrackingStream("Name\nAda")); }]
            : [];
        await using var reader = CreateReader().ToDbDataReader(openers);
        Assert.Throws<NotSupportedException>(() => _ = reader.HasRows);
        Assert.Throws<NotSupportedException>(() => reader.Read());
        Assert.That(calls, Is.Zero);
        Assert.That(await reader.ReadAsync(), Is.EqualTo(hasRows));
        Assert.That(reader.HasRows, Is.EqualTo(hasRows));
        Assert.Throws<NotSupportedException>(() => reader.Read());
    }

    [Test]
    public async Task ExistingBatchOverloads_AcceptStreamsFactoriesAndTypedNulls()
    {
        var csv = CreateReader();
        Assert.Throws<ArgumentNullException>(() => csv.ToDataReader((IEnumerable<Stream>)null!));
        Assert.Throws<ArgumentNullException>(() => csv.ToDbDataReader((IEnumerable<Func<Stream>>)null!));
        await using var streams = csv.ToDataReader([new TrackingStream("Name\nAda")]);
        await using var factories = csv.ToDbDataReader([() => new TrackingStream("Name\nGrace")]);
        Assert.That(await streams.ReadAsync(), Is.True);
        Assert.That(streams["Name"], Is.EqualTo("Ada"));
        Assert.That(await factories.ReadAsync(), Is.True);
        Assert.That(factories["Name"], Is.EqualTo("Grace"));
    }

    private sealed class TrackingStream(string content) : MemoryStream(Encoding.UTF8.GetBytes(content))
    {
        public int SyncDisposals { get; private set; }
        public int AsyncDisposals { get; private set; }
        public bool Readable { get; init; } = true;
        public bool FailOnRead { get; init; }
        public Func<CancellationToken, ValueTask<int>>? ReadOverride { get; init; }
        public override bool CanRead => Readable && base.CanRead;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => FailOnRead
                ? ValueTask.FromException<int>(new IOException("Cannot read the input."))
                : ReadOverride?.Invoke(cancellationToken) ?? base.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SyncDisposals++;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            base.Dispose(true);
            return ValueTask.CompletedTask;
        }
    }
}
