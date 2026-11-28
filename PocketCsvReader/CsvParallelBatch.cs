using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace PocketCsvReader;

internal readonly record struct CsvBatchInput(
    Func<CancellationToken, ValueTask<Stream>>? Open, string? FileName = null, Stream? Stream = null);

internal sealed class CsvParallelBatch : IAsyncDisposable
{
    private const int RowsPerBatch = 64;
    private const int BatchStorageLimit = 256 * 1024;
    private readonly CsvProfile _profile;
    private readonly IEnumerator<CsvBatchInput> _inputs;
    private readonly bool _ownsUnopenedStreams;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopToken;
    private readonly SemaphoreSlim _slots;
    private readonly Channel<Channel<RowBatch>>? _ordered;
    private readonly Channel<RowBatch>? _ready;
    private readonly CsvDataReader _cursor;
    private readonly Utf8FieldBuffer _decoder = new();
    private readonly object _lifecycle = new();
    private Task? _workers;
    private Task? _cleanup;
    private Task? _cancellation;
    private ExceptionDispatchInfo? _failure;
    private Channel<RowBatch>? _nextOrdered;
    private RowBatch? _currentBatch;
    private int _nextRow;
    private bool _ended;
    private long _bufferedStorage;
    private long _peakBufferedStorage;

    internal long PeakBufferedStorage => Interlocked.Read(ref _peakBufferedStorage);

    public CsvParallelBatch(IEnumerable<CsvBatchInput> inputs, CsvProfile profile, bool ownsUnopenedStreams)
    {
        _profile = profile.ForBatchInput();
        _inputs = inputs.GetEnumerator();
        _ownsUnopenedStreams = ownsUnopenedStreams;
        _stopToken = _stop.Token;
        _slots = new SemaphoreSlim(profile.BatchParallelism);
        _cursor = new CsvDataReader(Stream.Null, profile, leaveOpen: true);
        if (profile.PreserveBatchInputOrder)
            _ordered = CreateQueue<Channel<RowBatch>>(profile.BatchParallelism, singleWriter: true);
        else
            _ready = CreateQueue<RowBatch>(Math.Min(profile.BatchParallelism, 32), singleWriter: false);
    }

    public CsvDataReader? Current => CurrentSource is not null ? _cursor : null;
    public CsvRowSource? CurrentSource { get; private set; }

    public async ValueTask<bool> ReadAsync(bool useAsync, CancellationToken token)
    {
        bool windowComplete;
        lock (_lifecycle)
        {
            if (_ended)
                return false;
            CurrentSource = null;
            _cursor.ClearBatchRow();
            windowComplete = _cursor.Window?.IsComplete == true;
        }

        try
        {
            if (windowComplete)
            {
                await DisposeAsync().ConfigureAwait(false);
                return false;
            }

            using var registration = token.UnsafeRegister(static state => _ = ((CsvParallelBatch)state!).StopAsync(), this);
            lock (_lifecycle)
            {
                if (_cleanup is not null)
                    return false;
                _workers ??= Task.Run(() => RunAsync(useAsync));
            }

            while (true)
            {
                _stopToken.ThrowIfCancellationRequested();
                if (_currentBatch is not { } batch || _nextRow >= batch.Count)
                {
                    if (_currentBatch is { } consumed)
                    {
                        Interlocked.Add(ref _bufferedStorage, -consumed.StorageBytes);
                        _currentBatch = null;
                    }
                    var next = await ReadBatchAsync().ConfigureAwait(false);
                    if (next is null)
                    {
                        await DisposeAsync().ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        _failure?.Throw();
                        return false;
                    }
                    lock (_lifecycle)
                    {
                        if (_ended)
                            return false;
                        _currentBatch = batch = next;
                        _nextRow = 0;
                    }
                }

                lock (_lifecycle)
                {
                    if (_ended)
                        return false;
                    _stopToken.ThrowIfCancellationRequested();
                    var row = batch.Rows[_nextRow++];
                    if (_cursor.LoadBatchRow(row, batch.Headers, _decoder))
                    {
                        CurrentSource = row.Source;
                        return true;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_ended && !token.IsCancellationRequested && _failure is null)
        {
            return false;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _failure?.Throw();
            throw;
        }
    }

    private async ValueTask<RowBatch?> ReadBatchAsync()
    {
        if (_ready is { } ready)
            return await TakeAsync(ready.Reader).ConfigureAwait(false);

        while (true)
        {
            if (_nextOrdered is null)
            {
                _nextOrdered = await TakeAsync(_ordered!.Reader).ConfigureAwait(false);
                if (_nextOrdered is null)
                    return null;
            }
            if (await TakeAsync(_nextOrdered.Reader).ConfigureAwait(false) is { } batch)
                return batch;

            _nextOrdered = null;
            _slots.Release();
        }
    }

    private async ValueTask<T?> TakeAsync<T>(ChannelReader<T> reader)
        where T : class
    {
        while (await reader.WaitToReadAsync(_stopToken).ConfigureAwait(false))
        {
            _stopToken.ThrowIfCancellationRequested();
            if (reader.TryRead(out var item))
                return item;
        }
        return null;
    }

    private async Task RunAsync(bool useAsync)
    {
        var active = new List<Task>();
        var firstSchema = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            string[]? firstHeaders = null;
            for (var index = 0; ; index++)
            {
                await _slots.WaitAsync(_stopToken).ConfigureAwait(false);
                _stopToken.ThrowIfCancellationRequested();
                if (!_inputs.MoveNext())
                    break;
                var input = _inputs.Current;
                var inputIndex = index;
                var output = _ready ?? CreateQueue<RowBatch>(1, singleWriter: true);
                // The semaphore reserves a slot until its ordered output is drained.
                _ordered?.Writer.TryWrite(output);

                var inherited = index != 0 && _profile.Dialect.Header && !_profile.Dialect.HeaderRepeat
                    ? firstHeaders : null;
                var schema = index == 0 ? firstSchema : null;
                active.RemoveAll(static task => task.IsCompleted);
                active.Add(Task.Run(() => RunInputAsync(input, inputIndex, inherited, schema, output.Writer, useAsync)));

                if (schema is not null)
                    firstHeaders = await schema.Task.WaitAsync(_stopToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(new IOException("Could not enumerate CSV batch inputs.", exception));
        }
        finally
        {
            _ordered?.Writer.TryComplete();
            await Task.WhenAll(active).ConfigureAwait(false);
            try { await ReleaseInputsAsync().ConfigureAwait(false); }
            catch (Exception exception) { Fail(exception); }
            _ready?.Writer.TryComplete();
        }
    }

    private async Task RunInputAsync(CsvBatchInput input, int index, string[]? inherited,
        TaskCompletionSource<string[]?>? schema, ChannelWriter<RowBatch> output, bool useAsync)
    {
        var owned = new OwnedInput();
        try
        {
            // An eager stream has already transferred into this worker even if it was cancelled before starting.
            owned.Stream = input.Stream;
            _stopToken.ThrowIfCancellationRequested();
            if (owned.Stream is null)
            {
                var open = input.Open ?? throw new InvalidOperationException("A stream factory is null.");
                owned.Stream = await open(_stopToken).ConfigureAwait(false);
            }
            var stream = owned.Stream ?? throw new InvalidOperationException("A stream factory returned null.");
            if (!stream.CanRead)
                throw new InvalidOperationException("A stream factory returned an unreadable stream.");

            using var interrupt = useAsync ? default : _stopToken.UnsafeRegister(static state =>
                ((OwnedInput)state!).Interrupt(), owned);

            _stopToken.ThrowIfCancellationRequested();
            var reader = new CsvDataReader(stream, _profile, leaveOpen: true);
            try
            {
                await reader.PrepareBatchInputAsync(inherited, useAsync, _stopToken).ConfigureAwait(false);
                if (schema is not null && reader.SourceHeaders is { } headers)
                {
                    schema.TrySetResult(headers);
                    schema = null;
                }

                RowBatch? batch = null;
                while (!_stopToken.IsCancellationRequested && (useAsync
                    ? await reader.ReadBatchInputAsync(_stopToken).ConfigureAwait(false)
                    : reader.ReadBatchInput()))
                {
                    var sourceHeaders = reader.SourceHeaders!;
                    if (schema is not null)
                    {
                        schema.TrySetResult(sourceHeaders);
                        schema = null;
                    }

                    batch ??= new RowBatch(sourceHeaders);
                    batch.Add(reader.CopyBatchInput(index, input.FileName));
                    if (batch.Count == RowsPerBatch || batch.StorageBytes >= BatchStorageLimit)
                    {
                        await PublishAsync(output, batch).ConfigureAwait(false);
                        batch = null;
                    }
                }

                schema?.TrySetResult(reader.SourceHeaders);
                if (batch is not null)
                    await PublishAsync(output, batch).ConfigureAwait(false);
            }
            finally
            {
                if (useAsync)
                    await reader.DisposeAsync().ConfigureAwait(false);
                else
                    reader.Dispose();
            }
        }
        catch (Exception exception)
        {
            ReportInputFailure(exception, index, input.FileName);
        }
        finally
        {
            try { await owned.ReleaseAsync(useAsync).ConfigureAwait(false); }
            catch (Exception exception) { ReportInputFailure(exception, index, input.FileName); }
            if (_ordered is null)
                _slots.Release();
            else
                output.TryComplete();
        }
    }

    private void ReportInputFailure(Exception exception, int index, string? filename)
    {
        if (_stopToken.IsCancellationRequested)
            return;

        var source = filename is { } name ? $"'{name}' (input {index})" : $"input {index}";
        Fail(new IOException($"Failed to read CSV batch source {source}.", exception));
    }

    private async ValueTask PublishAsync(ChannelWriter<RowBatch> output, RowBatch batch)
    {
        // Include complete batches waiting to publish, queued, or being delivered.
        long bytes = Interlocked.Add(ref _bufferedStorage, batch.StorageBytes);
        long peak = Interlocked.Read(ref _peakBufferedStorage);
        while (bytes > peak)
        {
            long previous = Interlocked.CompareExchange(ref _peakBufferedStorage, bytes, peak);
            if (previous == peak)
                break;
            peak = previous;
        }
        await output.WriteAsync(batch, _stopToken).ConfigureAwait(false);
    }

    private async ValueTask ReleaseInputsAsync()
    {
        using var inputs = _inputs;
        if (_ownsUnopenedStreams)
        {
            ExceptionDispatchInfo? failure = null;
            while (inputs.MoveNext())
            {
                try
                {
                    if (inputs.Current.Stream is { } stream)
                        await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
        }
    }

    private void Fail(Exception exception)
    {
        Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);
        _ = StopAsync();
    }

    private Task StopAsync()
    {
        lock (_lifecycle)
            return _cancellation ??= _stop.CancelAsync();
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            return new(_cleanup ??= FinishAsync());
        }
    }

    private async Task FinishAsync()
    {
        _ended = true;
        var cancellation = StopAsync();
        try
        {
            var workers = _workers ?? ReleaseInputsAsync().AsTask();
            await Task.WhenAll(cancellation, workers).ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycle)
            {
                _currentBatch = null;
                CurrentSource = null;
                _cursor.ClearBatchRow();
                _cursor.Dispose();
                _decoder.Dispose();
            }
            if (_nextOrdered is { } next)
                Drain(next.Reader);
            if (_ordered is { } ordered)
            {
                while (ordered.Reader.TryRead(out var pending))
                    Drain(pending.Reader);
            }
            if (_ready is { } ready)
                Drain(ready.Reader);
            _stop.Dispose();
        }
    }

    private static void Drain(ChannelReader<RowBatch> reader)
    {
        while (reader.TryRead(out _)) { }
    }

    private static Channel<T> CreateQueue<T>(int capacity, bool singleWriter) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) { SingleWriter = singleWriter });

    private sealed class RowBatch(string[] headers)
    {
        public string[] Headers { get; } = headers;
        public CsvParallelRow[] Rows { get; } = new CsvParallelRow[RowsPerBatch];
        public int Count { get; private set; }
        public long StorageBytes { get; private set; } = RowsPerBatch * Unsafe.SizeOf<CsvParallelRow>();

        public void Add(CsvParallelRow row)
        {
            Rows[Count++] = row;
            StorageBytes += row.Record.StorageBytes;
        }
    }

    private sealed class OwnedInput
    {
        public Stream? Stream;

        public void Interrupt()
        {
            try { Interlocked.Exchange(ref Stream, null)?.Dispose(); }
            catch (Exception) { /* The worker observes the interrupted read. */ }
        }

        public async ValueTask ReleaseAsync(bool useAsync)
        {
            if (Interlocked.Exchange(ref Stream, null) is not { } stream)
                return;
            if (useAsync)
                await stream.DisposeAsync().ConfigureAwait(false);
            else
                stream.Dispose();
        }
    }
}
