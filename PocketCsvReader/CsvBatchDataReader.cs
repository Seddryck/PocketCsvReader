using System;
using System.Collections.Generic;
using System.Data;

namespace PocketCsvReader;
public class CsvBatchDataReader : IAsyncDataReader, IFieldValueReader, ISourceNullRecord
{
    private readonly bool _allStreamsOpen = false;
    private IEnumerator<Func<Stream>> Streams { get; } = null!;
    private readonly bool _usesOwnedFactories;
    private readonly bool _supportsSyncRead;
    private IEnumerator<Func<CancellationToken, ValueTask<ReaderInput>>>? _ownedFactories;
    private Func<CancellationToken, ValueTask<ReaderInput?>>? _nextAsyncInput;
    private Stream? _currentStream;
    private CsvProfile Profile { get; }
    private readonly RecordWindow? _window;
    private readonly CsvParallelBatch? _parallel;
    private readonly string[]? _filenames;

    private CsvDataReader? Current { get; set; }
    private bool _isClosed = false;
    private int fileCount = 0;
    private int _readInProgress;
    private bool _hasCurrentSource;
    private CsvDataReader? Active => _parallel is { } parallel ? parallel.Current : Current;

    /// <summary>Gets the source of the current row, or null when the cursor is not on a row.</summary>
    /// <value>The original input and record number of the current row.</value>
    public CsvRowSource? CurrentSource
    {
        get
        {
            if (_isClosed || !_hasCurrentSource)
                return null;
            if (_parallel is { } parallel)
                return parallel.CurrentSource;
            if (Current is not { } current)
                return null;

            var inputIndex = fileCount - 1;
            return new(inputIndex, _filenames?[inputIndex],
                current.CurrentSourceRecordNumber ?? current.RowCount, current.CurrentPosition);
        }
    }

    /// <summary>Gets the parser position of the current row when tracking is enabled.</summary>
    /// <value>The current record start, when available.</value>
    public RecordPosition? CurrentPosition => CurrentSource?.Position;
    internal bool HasRowNumber => Profile.RowNumber is not null;

    public CsvBatchDataReader(IEnumerable<Stream> streams, CsvProfile profile)
    {
        Profile = profile.ForBatchInput();
        if (profile.BatchParallelism > 1)
        {
            _supportsSyncRead = true;
            _parallel = new(streams.Select(static stream => new CsvBatchInput(null, Stream: stream)),
                profile, ownsUnopenedStreams: true);
            return;
        }

        _allStreamsOpen = true;
        Streams = streams.Select<Stream, Func<Stream>>(stream => () => stream).GetEnumerator();
        _window = RecordWindow.Create(profile);
        if (_window?.IsComplete != true)
            MoveNext();
    }

    public CsvBatchDataReader(IEnumerable<Func<Stream>> streams, CsvProfile profile)
        : this(streams, profile, filenames: null) { }

    internal CsvBatchDataReader(IEnumerable<Func<Stream>> streams, CsvProfile profile, string[]? filenames)
    {
        Profile = profile.ForBatchInput();
        _filenames = filenames;
        if (profile.BatchParallelism > 1)
        {
            _supportsSyncRead = true;
            var inputs = streams.Select((open, index) =>
                new CsvBatchInput(_ => ValueTask.FromResult(open()), filenames?[index]));
            _parallel = new(inputs, profile, ownsUnopenedStreams: false);
            return;
        }

        Streams = streams.GetEnumerator();
        _window = RecordWindow.Create(profile);
        if (_window?.IsComplete != true)
            MoveNext();
    }

    private CsvBatchDataReader(
        IEnumerable<Func<CancellationToken, ValueTask<ReaderInput>>> openers, CsvProfile profile, bool supportsSyncRead)
        : this(profile, supportsSyncRead)
    {
        if (_window?.IsComplete != true)
            _ownedFactories = openers.GetEnumerator();
    }

    private CsvBatchDataReader(CsvProfile profile, bool supportsSyncRead)
    {
        Profile = profile.ForBatchInput();
        _usesOwnedFactories = true;
        _supportsSyncRead = supportsSyncRead;
        _window = RecordWindow.Create(profile);
    }

    private CsvBatchDataReader(IEnumerable<CsvBatchInput> inputs, CsvProfile profile)
    {
        Profile = profile;
        _parallel = new(inputs, profile, ownsUnopenedStreams: false);
    }

    internal static CsvBatchDataReader FromAsyncInputs(
        Func<CancellationToken, ValueTask<ReaderInput?>> next, CsvProfile profile)
    {
        var reader = new CsvBatchDataReader(profile, supportsSyncRead: false);
        if (reader._window?.IsComplete != true)
            reader._nextAsyncInput = next;
        return reader;
    }

    /// <summary>Creates a batch whose stream factories are awaited on demand by <see cref="ReadAsync"/>.</summary>
    /// <remarks>
    /// Each factory receives the active read token and transfers ownership of its readable stream to
    /// the batch. A failed or cancelled acquisition or record read closes the batch. Synchronous
    /// <see cref="Read"/> is unsupported.
    /// </remarks>
    public static CsvBatchDataReader FromAsyncStreamFactories(
        IEnumerable<Func<CancellationToken, ValueTask<Stream>>> openers, CsvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(openers);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.BatchParallelism > 1)
            return new CsvBatchDataReader(openers.Select(open => new CsvBatchInput(open)), profile);
        return new CsvBatchDataReader(openers.Select<Func<CancellationToken, ValueTask<Stream>>, Func<CancellationToken, ValueTask<ReaderInput>>>(
            open => token => OpenInput(open, token)), profile, supportsSyncRead: false);

        static async ValueTask<ReaderInput> OpenInput(Func<CancellationToken, ValueTask<Stream>> open, CancellationToken token)
        {
            if (open is null)
                throw new InvalidOperationException("An asynchronous stream factory is null.");
            return new(await open(token).ConfigureAwait(false));
        }
    }

    internal static CsvBatchDataReader FromDeferredInputs(IEnumerable<Func<ReaderInput>> openers, CsvProfile profile)
        => new(openers.Select<Func<ReaderInput>, Func<CancellationToken, ValueTask<ReaderInput>>>(
            open => _ => ValueTask.FromResult(open())), profile, supportsSyncRead: true);

    internal readonly record struct ReaderInput(Stream? Stream, Func<Stream, CsvDataReader>? CreateReader = null,
        int? SkippedRows = null);

    private void MoveNext()
    {
        fileCount++;
        var fields = Current?.SourceHeaders;
        var rowNumber = Current?.CurrentRowNumber ?? 0;

        Current?.Dispose();
        _currentStream?.Dispose();

        if (Streams.MoveNext())
        {
            _currentStream = Streams.Current.Invoke();
            Current = new CsvDataReader(_currentStream, Profile) { CurrentRowNumber = rowNumber, Window = _window };
        }
        else
        {
            Current = null;
            _currentStream?.Close();
        }

        if (Current is not null && fields is not null && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            Current!.SetHeaders(fields);
    }

    public bool Read()
    {
        EnterRead();
        _hasCurrentSource = false;
        try
        {
            if (_parallel is { } parallel)
            {
                if (!_supportsSyncRead)
                    throw new NotSupportedException("Batch readers with asynchronous inputs require ReadAsync.");
                try { return _hasCurrentSource = parallel.ReadAsync(useAsync: false, default).AsTask().GetAwaiter().GetResult(); }
                catch { _isClosed = true; throw; }
            }

            if (_usesOwnedFactories)
            {
                if (!_supportsSyncRead)
                    throw new NotSupportedException("Batch readers with asynchronous inputs require ReadAsync.");
                return _hasCurrentSource = ReadFromDeferredFactories();
            }

            if (Current is null)
                return false;

            while (!Current.Read())
            {
                if (_window?.IsComplete == true)
                    return false;
                MoveNext();
                if (Current is null)
                    return false;
            }
            return _hasCurrentSource = true;
        }
        finally
        {
            ExitRead();
        }
    }

    public async Task<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hasCurrentSource = false;
            if (_parallel is { } parallel)
            {
                try { return _hasCurrentSource = await parallel.ReadAsync(useAsync: true, cancellationToken).ConfigureAwait(false); }
                catch { _isClosed = true; throw; }
            }

            if (_usesOwnedFactories)
                return _hasCurrentSource = await ReadFromOwnedFactoriesAsync(cancellationToken).ConfigureAwait(false);

            if (Current is null)
                return false;

            while (!await Current.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_window?.IsComplete == true)
                    return false;
                await MoveNextAsync().ConfigureAwait(false);
                if (Current is null)
                    return false;
            }
            return _hasCurrentSource = true;
        }
        finally
        {
            ExitRead();
        }
    }

    private bool ReadFromDeferredFactories()
    {
        try
        {
            while (_ownedFactories is { } factories)
            {
                if (Current is not null && Current.Read())
                    return true;

                if (!MoveNextFromDeferredFactories(factories))
                    return false;
            }
            return false;
        }
        catch
        {
            _isClosed = true;
            try
            {
                DisposeOwnedBatch();
            }
            catch
            {
                // Preserve the acquisition or read error if cleanup also fails.
            }
            throw;
        }
    }

    private bool MoveNextFromDeferredFactories(IEnumerator<Func<CancellationToken, ValueTask<ReaderInput>>> factories)
    {
        var fields = Current?.SourceHeaders;
        var rowNumber = Current?.BatchRowNumber ?? 0;
        DisposeOwnedInput();

        while (_window?.IsComplete != true && factories.MoveNext())
        {
            var acquisition = factories.Current(CancellationToken.None);
            if (!acquisition.IsCompletedSuccessfully)
                throw new InvalidOperationException("A deferred synchronous factory must complete synchronously.");

            var input = acquisition.Result;
            if (input.SkippedRows is int skipped)
            {
                if (HasRowNumber)
                    rowNumber = checked(rowNumber + skipped);
                continue;
            }

            _currentStream = input.Stream;
            SetOwnedCurrent(fields, rowNumber, input.CreateReader);
            return true;
        }

        DisposeOwnedFactories();
        return false;
    }

    private async ValueTask<bool> ReadFromOwnedFactoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_ownedFactories is not null || _nextAsyncInput is not null)
            {
                if (Current is not null && await Current.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return true;

                if (_window?.IsComplete == true)
                {
                    await DisposeOwnedBatchAsync().ConfigureAwait(false);
                    return false;
                }

                if (!await MoveNextFromOwnedFactoriesAsync(cancellationToken).ConfigureAwait(false))
                    return false;
            }
            return false;
        }
        catch
        {
            _isClosed = true;
            try
            {
                await DisposeOwnedBatchAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the acquisition or read error if cleanup also fails.
            }
            throw;
        }
    }

    private async ValueTask<bool> MoveNextFromOwnedFactoriesAsync(CancellationToken cancellationToken)
    {
        var fields = Current?.SourceHeaders;
        var rowNumber = Current?.BatchRowNumber ?? 0;
        await DisposeOwnedInputAsync().ConfigureAwait(false);

        while (true)
        {
            ObjectDisposedException.ThrowIf(_isClosed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var input = _nextAsyncInput is { } next
                ? await next(cancellationToken).ConfigureAwait(false)
                : await OpenNextFactoryAsync(cancellationToken).ConfigureAwait(false);
            if (input is not { } acquired)
            {
                DisposeOwnedFactories();
                return false;
            }
            if (acquired.SkippedRows is int skipped)
            {
                if (HasRowNumber)
                    rowNumber = checked(rowNumber + skipped);
                continue;
            }

            _currentStream = acquired.Stream;
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_isClosed, this);
            SetOwnedCurrent(fields, rowNumber, acquired.CreateReader);
            return true;
        }
    }

    private async ValueTask<ReaderInput?> OpenNextFactoryAsync(CancellationToken cancellationToken)
    {
        if (_ownedFactories is not { } factories || !factories.MoveNext())
            return null;

        var open = factories.Current
            ?? throw new InvalidOperationException("An asynchronous stream factory is null.");
        cancellationToken.ThrowIfCancellationRequested();
        return await open(cancellationToken).ConfigureAwait(false);
    }

    private void SetOwnedCurrent(string[]? fields, int rowNumber, Func<Stream, CsvDataReader>? createReader)
    {
        var stream = _currentStream ?? throw new InvalidOperationException("A stream factory returned null.");
        if (!stream.CanRead)
            throw new InvalidOperationException("A stream factory returned an unreadable stream.");

        fileCount++;
        Current = createReader?.Invoke(stream) ?? new CsvDataReader(stream, Profile, leaveOpen: true);
        Current.CurrentRowNumber = rowNumber;
        Current.Window = _window;
        if (fields is not null && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            Current.SetHeaders(fields);
    }

    private void DisposeOwnedInput()
    {
        var reader = Current;
        var stream = _currentStream;
        Current = null;
        _currentStream = null;
        try
        {
            reader?.Dispose();
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private void DisposeOwnedBatch()
    {
        try
        {
            DisposeOwnedInput();
        }
        finally
        {
            DisposeOwnedFactories();
        }
    }

    private async ValueTask DisposeOwnedInputAsync()
    {
        var reader = Current;
        var stream = _currentStream;
        Current = null;
        _currentStream = null;
        try
        {
            if (reader is not null)
                await reader.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void DisposeOwnedFactories()
    {
        var factories = _ownedFactories;
        _ownedFactories = null;
        _nextAsyncInput = null;
        factories?.Dispose();
    }

    private async ValueTask DisposeOwnedBatchAsync()
    {
        try
        {
            await DisposeOwnedInputAsync().ConfigureAwait(false);
        }
        finally
        {
            DisposeOwnedFactories();
        }
    }

    private async ValueTask MoveNextAsync()
    {
        fileCount++;
        var fields = Current?.SourceHeaders;
        var rowNumber = Current?.CurrentRowNumber ?? 0;

        if (Current is not null)
            await Current.DisposeAsync().ConfigureAwait(false);
        if (_currentStream is not null)
            await _currentStream.DisposeAsync().ConfigureAwait(false);

        if (Streams.MoveNext())
        {
            _currentStream = Streams.Current.Invoke();
            Current = new CsvDataReader(_currentStream, Profile) { CurrentRowNumber = rowNumber, Window = _window };
        }
        else
        {
            Current = null;
            _currentStream = null;
        }

        if (Current is not null && fields is not null && Profile.Dialect.Header && !Profile.Dialect.HeaderRepeat)
            Current.SetHeaders(fields);
    }

    private void EnterRead()
    {
        if (Interlocked.CompareExchange(ref _readInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent Read and ReadAsync calls are not supported.");
    }

    private void ExitRead() => Volatile.Write(ref _readInProgress, 0);

    #region composite
    public int FieldCount => Active?.FieldCount ?? 0;
    public object GetValue(int i) => Active!.GetValue(i);
    /// <summary>Validates the current cell with the active reader's schema and cached parsers.</summary>
    public bool IsValid(int i) => (Active ?? throw new InvalidOperationException("No current record is available.")).IsValid(i);
    /// <summary>Validates the named current cell with the active reader's schema and cached parsers.</summary>
    public bool IsValid(string name) => (Active ?? throw new InvalidOperationException("No current record is available.")).IsValid(name);
    public string GetName(int i) => Active!.GetName(i);
    public int GetOrdinal(string name) => Active!.GetOrdinal(name);
    public Type GetFieldType(int i) => Active!.GetFieldType(i);
    public bool IsDBNull(int i) => Active!.IsDBNull(i);

    /// <inheritdoc cref="BaseDataRecord{CsvProfile}.IsSourceNull(int)"/>
    public bool IsSourceNull(int ordinal) => Active!.IsSourceNull(ordinal);

    /// <inheritdoc cref="BaseDataRecord{CsvProfile}.IsSourceNull(string)"/>
    public bool IsSourceNull(string name) => IsSourceNull(GetOrdinal(name));
    public object this[int i] => Active![i];
    public object this[string name] => Active![name];
    public int RecordsAffected => -1;
    public int Depth => 0;
    public bool NextResult() => false;
    public DataTable GetSchemaTable() => throw new NotImplementedException();
    public int GetInt32(int i) => Active!.GetInt32(i);
    public string GetString(int i) => Active!.GetString(i);
    public bool GetBoolean(int i) => Active!.GetBoolean(i);
    public long GetInt64(int i) => Active!.GetInt64(i);
    public double GetDouble(int i) => Active!.GetDouble(i);
    public float GetFloat(int i) => Active!.GetFloat(i);
    public Guid GetGuid(int i) => Active!.GetGuid(i);
    public short GetInt16(int i) => Active!.GetInt16(i);
    public byte GetByte(int i) => Active!.GetByte(i);
    public char GetChar(int i) => Active!.GetChar(i);
    public DateTime GetDateTime(int i) => Active!.GetDateTime(i);
    public decimal GetDecimal(int i) => Active!.GetDecimal(i);
    public long GetBytes(int i, long fieldoffset, byte[]? buffer, int bufferoffset, int length)
        => Active!.GetBytes(i, fieldoffset, buffer, bufferoffset, length);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        => Active!.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    public IDataReader GetData(int i) => Active!.GetData(i);
    public string GetDataTypeName(int i) => Active!.GetDataTypeName(i);
    public int GetValues(object[] values) => Active!.GetValues(values);

    public T GetFieldValue<T>(int i) => Active!.GetFieldValue<T>(i);

    #endregion
    internal CsvDataReader? CurrentCsvRow => _hasCurrentSource ? Active : null;
    internal long PeakBufferedStorage => _parallel?.PeakBufferedStorage ?? 0;

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            if (_parallel is { } parallel)
            {
                parallel.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }

            if (_usesOwnedFactories)
            {
                DisposeOwnedBatch();
                return;
            }

            Current?.Dispose();
            _currentStream?.Dispose();
            if (_allStreamsOpen)
            {
                while (Streams.MoveNext())
                    Streams.Current.Invoke().Dispose();
            }
            (Streams as IDisposable)?.Dispose();
        }
    }

    public bool IsClosed => _isClosed;

    private bool _disposed = false;
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _isClosed = true;

        if (_parallel is { } parallel)
        {
            await parallel.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
            return;
        }

        if (_usesOwnedFactories)
        {
            await DisposeOwnedBatchAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
            return;
        }

        if (Current is not null)
            await Current.DisposeAsync().ConfigureAwait(false);
        if (_currentStream is not null)
            await _currentStream.DisposeAsync().ConfigureAwait(false);
        if (_allStreamsOpen)
        {
            while (Streams.MoveNext())
                await Streams.Current.Invoke().DisposeAsync().ConfigureAwait(false);
        }
        (Streams as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            Close();
        }
    }

    ~CsvBatchDataReader()
    {
        Dispose(false);
    }
}
