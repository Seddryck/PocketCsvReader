using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvParallelBatchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    public async Task OrderedMatchesSequentialWithProjectionWindowAndSourcePositions(CsvInputMode mode, bool useAsync)
    {
        async Task<List<(int Number, string Name, int Id, CsvRowSource Source)>> Read(int workers)
        {
            var csv = new CsvReaderBuilder()
                .WithDialect(d => d.WithHeader().WithHeaderRepeat(false).WithLineTerminator("\n"))
                .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
                .WithSchema(s => s.Indexed().WithField<int>().WithField<string>().WithField<string>())
                .WithProjection("Name", "Id")
                .WithPositionTracking()
                .WithRowNumber()
                .WithFilter<string>("Include", value => value == "yes")
                .WithSkip(1)
                .WithTake(3)
                .WithBatchParallelism(workers)
                .Build();

            Func<Stream>[] inputs =
            [
                () => StreamOf("Id,Name,Include\n1,Skip,yes\n2,Filtered,no\n3,\"line\né\",yes\n"),
                () => StreamOf(""),
                () => StreamOf("4,Grace,yes\n5,\"A\"\"B\",yes\n6,Extra,yes")
            ];

            await using var reader = csv.ToDataReader(inputs);
            var actual = new List<(int, string, int, CsvRowSource)>();
            while (useAsync ? await reader.ReadAsync().WaitAsync(Timeout) : reader.Read())
                actual.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.CurrentSource!.Value));
            Assert.That(reader.CurrentSource, Is.Null);
            return actual;
        }

        var expected = await Read(1);
        var actual = await Read(3);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual.Select(row => row.Number), Is.EqualTo(new[] { 3, 4, 5 }));
        Assert.That(actual.Select(row => row.Name), Is.EqualTo(new[] { "line\né", "Grace", "A\"B" }));
        Assert.That(actual.Select(row => row.Source.InputIndex), Is.EqualTo(new[] { 0, 2, 2 }));
        Assert.That(actual.Select(row => row.Source.RecordNumber), Is.EqualTo(new[] { 3L, 1L, 2L }));
        Assert.That(actual[0].Source.Position!.Value.PhysicalLine, Is.EqualTo(4));
    }

    [Test]
    public async Task UnorderedAsyncFactoriesDeliverLaterFileWhileFirstIsStalled()
    {
        using var first = new GatedStream("Value\n", "A\nB\n");
        var csv = CreateReader(preserveOrder: false);
        Func<CancellationToken, ValueTask<Stream>>[] inputs =
        [
            _ => ValueTask.FromResult<Stream>(first),
            _ => ValueTask.FromResult<Stream>(StreamOf("C\nD\n"))
        ];
        await using var reader = (DbDataReaderAdapter)csv.ToDbDataReader(inputs);
        Assert.Throws<NotSupportedException>(() => reader.Read());
        try
        {
            Assert.That(await reader.ReadAsync().WaitAsync(Timeout), Is.True);
            Assert.That(reader.GetString(1), Is.EqualTo("C"));
            Assert.That(reader.CurrentSource, Is.EqualTo(new CsvRowSource(1, null, 1, null)));
            Assert.That(await reader.ReadAsync().WaitAsync(Timeout), Is.True);
            Assert.That(reader.GetString(1), Is.EqualTo("D"));
            Assert.That(reader.CurrentSource!.Value.RecordNumber, Is.EqualTo(2));
        }
        finally
        {
            first.Resume();
        }

        var rest = new List<(int, string, int, long)>();
        while (await reader.ReadAsync().WaitAsync(Timeout))
        {
            var source = reader.CurrentSource!.Value;
            rest.Add((reader.GetInt32(0), reader.GetString(1), source.InputIndex, source.RecordNumber));
        }
        Assert.That(rest, Is.EqualTo(new[] { (3, "A", 0, 1L), (4, "B", 0, 2L) }));
        Assert.That(reader.CurrentSource, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CancellationUnblocksWorkersAndDisposesStreams(bool preserveOrder)
    {
        using var first = new GatedStream("Value\n", "");
        var csv = CreateReader(preserveOrder);
        Func<Stream>[] inputs = [() => first, () => StreamOf("")];
        await using var reader = csv.ToDataReader(inputs);
        using var cancellation = new CancellationTokenSource();
        var advancing = reader.ReadAsync(cancellation.Token);
        await first.Stalled.WaitAsync(Timeout);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await advancing.WaitAsync(Timeout));
        Assert.That(first.CanRead, Is.False);
        Assert.That(reader.IsClosed, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SlowConsumerKeepsFactoriesAndReadsBoundedAndDisposalReleasesStreams(bool preserveOrder)
    {
        var csv = CreateReader(preserveOrder);
        using var first = new GatedStream("Value\n", string.Concat(Enumerable.Repeat("X\n", 3000)));
        using var second = new GatedStream("", string.Concat(Enumerable.Repeat("Y\n", 3000)));
        var opened = 0;
        Func<Stream>[] inputs =
        [
            () => { Interlocked.Increment(ref opened); first.Resume(); return first; },
            () => { Interlocked.Increment(ref opened); second.Resume(); return second; },
            () => { Interlocked.Increment(ref opened); return StreamOf("Z"); }
        ];
        await using var reader = csv.ToDataReader(inputs);
        Assert.That(await reader.ReadAsync().WaitAsync(Timeout), Is.True);
        await second.Stalled.WaitAsync(Timeout);
        await reader.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.That(opened, Is.EqualTo(2));
        Assert.That(first.CanRead, Is.False);
        Assert.That(second.CanRead, Is.False);
        Assert.That(first.BytesRead, Is.LessThan(6006));
        Assert.That(second.BytesRead, Is.LessThan(6000));
    }

    [Test]
    public async Task FailureNamesTheInputAndClosesAStalledPeer()
    {
        var csv = CreateReader(preserveOrder: true);
        using var first = new GatedStream("Value\n", "");
        Func<Stream>[] inputs = [() => first, () => throw new InvalidOperationException("Test acquisition failure")];
        await using var reader = csv.ToDataReader(inputs);
        var failure = await Assert.ThrowsAsync<IOException>(async () => await reader.ReadAsync().WaitAsync(Timeout));
        Assert.That(failure!.Message, Does.Contain("input 1"));
        Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(first.CanRead, Is.False);
    }

    [Test]
    public void DbPrefetchHidesProvenanceAndReportsFilenames()
    {
        var csv = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithBatchParallelism(2).Build();
        var paths = new[] { Path.GetTempFileName(), Path.GetTempFileName() };
        try
        {
            File.WriteAllText(paths[0], "Name\nAda\n");
            File.WriteAllText(paths[1], "Name\nGrace\n");
            using var db = (DbDataReaderAdapter)csv.ToDbDataReader(paths);
            Assert.That(db.HasRows, Is.True);
            Assert.That(db.CurrentSource, Is.Null);
            Assert.That(db.Read(), Is.True);
            Assert.That(db.CurrentSource, Is.EqualTo(new CsvRowSource(0, paths[0], 1, null)));
            Assert.That(db.Read(), Is.True);
            Assert.That(db.CurrentSource, Is.EqualTo(new CsvRowSource(1, paths[1], 1, null)));
        }
        finally
        {
            foreach (var path in paths)
                File.Delete(path);
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void SkippedMalformedRecordsStillCountInTheSource(int workers)
    {
        var csv = new CsvReaderBuilder().WithDialect(d => d.WithoutHeader().WithQuoteChar('"').WithLineTerminator("\n"))
            .WithBatchParallelism(workers).Build();
        var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<BadDataContext>();
        csv.Profile.BadDataPolicy = new BadDataPolicy(context =>
        {
            diagnostics.Enqueue(context);
            return BadDataAction.SkipRecord;
        }, maximumErrors: 2);
        Func<Stream>[] inputs = [() => StreamOf("a,b\n\"bad\"x,z\nc,d"), () => StreamOf("e,f")];
        using var reader = csv.ToDataReader(inputs);
        var sources = new List<CsvRowSource>();
        while (reader.Read())
            sources.Add(reader.CurrentSource!.Value);

        Assert.That(sources, Is.EqualTo(new CsvRowSource[] { new(0, null, 1, null), new(0, null, 3, null), new(1, null, 1, null) }));
        Assert.That(diagnostics.Single().RecordPosition, Is.Null);
    }

    [Test]
    public async Task DisposeBeforeReadClosesAllSuppliedStreams()
    {
        var first = StreamOf("Value\nA");
        var second = StreamOf("B");
        var csv = CreateReader(preserveOrder: false);
        await csv.ToDataReader(new Stream[] { first, second }).DisposeAsync();
        Assert.That(first.CanRead || second.CanRead, Is.False);
    }

    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    public async Task PackedProjectionPreservesReorderingDuplicatesAndMissingCells(CsvInputMode mode, bool useAsync)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithHeaderRepeat(true).WithLineTerminator("\n"))
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection("Name", "Id", "Name")
            .WithRowNumber()
            .WithBatchParallelism(2)
            .Build();
        var omitted = new string('x', 40_000);
        Func<Stream>[] inputs =
        [
            () => StreamOf($"Id,Ignored,Name\n1,\"{omitted}\nunused\",\"A\"\"B\"\n2,{omitted}\n"),
            () => StreamOf($"Name,Ignored,Id\n東京,{omitted},3\n")
        ];

        await using var reader = csv.ToDataReader(inputs);
        var rows = new List<(int Number, string Name, int Id, string Duplicate)>();
        while (useAsync ? await reader.ReadAsync().WaitAsync(Timeout) : reader.Read())
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));

        Assert.That(rows, Is.EqualTo(new[] { (1, "A\"B", 1, "A\"B"), (2, "", 2, ""), (3, "東京", 3, "東京") }));
        Assert.That(reader.PeakBufferedStorage, Is.LessThan(32_000));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task BatchBuffersPreserveRowsAcrossGrowthAndOversizedRecords(CsvInputMode mode)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithBatchParallelism(2, preserveInputOrder: false)
            .Build();
        var longValue = new string('z', 20_000);
        var oversized = new string('q', 300_000);
        var rows = Enumerable.Range(0, 140).Select(i => i switch
        {
            9 => longValue,
            81 => oversized,
            _ => $"value-{i}"
        }).ToArray();
        var first = "Name\n" + string.Join('\n', rows);
        var second = "Name\n" + string.Join('\n', rows.Reverse());

        await using var reader = csv.ToDataReader(new[] { StreamOf(first), StreamOf(second) });
        var seen = new HashSet<(int, long)>();
        while (await reader.ReadAsync().WaitAsync(Timeout))
        {
            var source = reader.CurrentSource!.Value;
            Assert.That(seen.Add((source.InputIndex, source.RecordNumber)), Is.True);
            int index = checked((int)source.RecordNumber - 1);
            Assert.That(reader.GetString(0), Is.EqualTo(rows[source.InputIndex == 0 ? index : rows.Length - 1 - index]));
        }

        Assert.That(seen.Count, Is.EqualTo(2 * rows.Length));
    }

    [Test]
    public void PooledExpandedFieldsPreserveNestedArrayMetadata()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n")
                .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')))
            .WithProjection("Values")
            .WithBatchParallelism(2)
            .Build();
        const int rowsPerInput = 80;
        var input = "Name,Values\n" + string.Join('\n',
            Enumerable.Range(0, rowsPerInput).Select(i => $"value,[{i}|{i + 1}]"));
        using var reader = csv.ToDataReader(new[] { StreamOf(input), StreamOf(input) });

        int count = 0;
        while (reader.Read())
        {
            int value = count++ % rowsPerInput;
            Assert.That(reader.CurrentCsvRow!.GetArray<int>(0), Is.EqualTo(new[] { value, value + 1 }));
        }
        Assert.That(count, Is.EqualTo(2 * rowsPerInput));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposeWhileReadIsWaitingReleasesTheActiveInputs(bool preserveOrder)
    {
        var csv = CreateReader(preserveOrder);
        using var first = new GatedStream("Value\n", "");
        Func<Stream>[] inputs = [() => first, () => StreamOf("")];
        await using var reader = csv.ToDataReader(inputs);

        var waiting = reader.ReadAsync();
        await first.Stalled.WaitAsync(Timeout);
        await reader.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.That(await waiting.WaitAsync(Timeout), Is.False);
        Assert.That(first.CanRead, Is.False);
    }

    [Test]
    public void BuilderRejectsNonpositiveConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvReaderBuilder().WithBatchParallelism(0));
    }

    private static CsvReader CreateReader(bool preserveOrder) => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithHeaderRepeat(false).WithLineTerminator("\n"))
        .WithStreamInitialization(StreamInitializationOptions.Utf8)
        .WithRowNumber()
        .WithBatchParallelism(2, preserveOrder)
        .Build();

    private static MemoryStream StreamOf(string text) => new(Encoding.UTF8.GetBytes(text));

    private sealed class GatedStream(string initial, string remainder) : Stream
    {
        private readonly MemoryStream _initial = StreamOf(initial);
        private readonly MemoryStream _remainder = StreamOf(remainder);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _closed;

        public Task Stalled => _stalled.Task;
        public int BytesRead { get; private set; }
        public void Resume() => _resume.TrySetResult();
        public override bool CanRead => !_closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = _initial.Read(buffer.Span);
            if (count == 0)
            {
                _stalled.TrySetResult();
                await _resume.Task.WaitAsync(cancellationToken);
                count = _remainder.Read(buffer.Span[..Math.Min(buffer.Length, 8)]);
            }
            BytesRead += count;
            return count;
        }

        protected override void Dispose(bool disposing)
        {
            _closed = true;
            if (disposing)
            {
                _initial.Dispose();
                _remainder.Dispose();
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
