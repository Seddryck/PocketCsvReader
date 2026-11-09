using System;
using System.Collections.Generic;
using System.Data;

namespace PocketCsvReader;
public class CsvBatchDataReader : IAsyncDataReader, IFieldValueReader
{
    private readonly bool _allStreamsOpen = false;
    private IEnumerator<Func<Stream>> Streams { get; } = null!;
    private readonly bool _usesOwnedFactories;
    private readonly bool _supportsSyncRead;
    private IEnumerator<Func<CancellationToken, ValueTask<Stream>>>? _ownedFactories;
    private Stream? _currentStream;
    private CsvProfile Profile { get; }
    private readonly RecordWindow? _window;

    private CsvDataReader? Current { get; set; }
    private bool _isClosed = false;
    private int fileCount = 0;
    private int _readInProgress;
    internal bool HasRowNumber => Profile.RowNumber is not null;

    public CsvBatchDataReader(IEnumerable<Stream> streams, CsvProfile profile)
        : this(streams.Select<Stream, Func<Stream>>(x => () => x), profile)
    {
        _allStreamsOpen = true;
    }

    public CsvBatchDataReader(IEnumerable<Func<Stream>> streams, CsvProfile profile)
    {
        (Streams, Profile) = (streams.GetEnumerator(), profile);
        _window = RecordWindow.Create(profile);
        if (_window?.IsComplete != true)
            MoveNext();
    }

    private CsvBatchDataReader(
        IEnumerable<Func<CancellationToken, ValueTask<Stream>>> openers, CsvProfile profile, bool supportsSyncRead)
    {
        Profile = profile;
        _usesOwnedFactories = true;
        _supportsSyncRead = supportsSyncRead;
        _window = RecordWindow.Create(profile);
        if (_window?.IsComplete != true)
            _ownedFactories = openers.GetEnumerator();
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
        return new CsvBatchDataReader(openers, profile, supportsSyncRead: false);
    }

    internal static CsvBatchDataReader FromDeferredStreamFactories(IEnumerable<Func<Stream>> openers, CsvProfile profile)
        => new(openers.Select<Func<Stream>, Func<CancellationToken, ValueTask<Stream>>>(
            open => _ => ValueTask.FromResult(open())), profile, supportsSyncRead: true);

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
        try
        {
            if (_usesOwnedFactories)
            {
                if (!_supportsSyncRead)
                    throw new NotSupportedException("Batch readers with asynchronous stream factories require ReadAsync.");
                return ReadFromDeferredFactories();
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
            return true;
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
            if (_usesOwnedFactories)
                return await ReadFromOwnedFactoriesAsync(cancellationToken).ConfigureAwait(false);

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
            return true;
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

                var fields = Current?.SourceHeaders;
                var rowNumber = Current?.CurrentRowNumber ?? 0;
                DisposeOwnedInput();
                if (_window?.IsComplete == true || !factories.MoveNext())
                {
                    DisposeOwnedFactories();
                    return false;
                }

                var acquisition = factories.Current(CancellationToken.None);
                if (!acquisition.IsCompletedSuccessfully)
                    throw new InvalidOperationException("A deferred synchronous factory must complete synchronously.");
                _currentStream = acquisition.Result;
                SetOwnedCurrent(fields, rowNumber);
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

    private async ValueTask<bool> ReadFromOwnedFactoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_ownedFactories is { } factories)
            {
                if (Current is not null && await Current.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return true;

                if (_window?.IsComplete == true)
                {
                    await DisposeOwnedBatchAsync().ConfigureAwait(false);
                    return false;
                }

                if (!await MoveNextFromOwnedFactoriesAsync(factories, cancellationToken).ConfigureAwait(false))
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

    private async ValueTask<bool> MoveNextFromOwnedFactoriesAsync(
        IEnumerator<Func<CancellationToken, ValueTask<Stream>>> factories,
        CancellationToken cancellationToken)
    {
        var fields = Current?.SourceHeaders;
        var rowNumber = Current?.CurrentRowNumber ?? 0;
        await DisposeOwnedInputAsync().ConfigureAwait(false);

        ObjectDisposedException.ThrowIf(_isClosed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!factories.MoveNext())
        {
            DisposeOwnedFactories();
            return false;
        }

        var open = factories.Current
            ?? throw new InvalidOperationException("An asynchronous stream factory is null.");
        cancellationToken.ThrowIfCancellationRequested();
        _currentStream = await open(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_isClosed, this);
        SetOwnedCurrent(fields, rowNumber);
        return true;
    }

    private void SetOwnedCurrent(string[]? fields, int rowNumber)
    {
        var stream = _currentStream ?? throw new InvalidOperationException("A stream factory returned null.");
        if (!stream.CanRead)
            throw new InvalidOperationException("A stream factory returned an unreadable stream.");

        Current = new CsvDataReader(stream, Profile, leaveOpen: true) { CurrentRowNumber = rowNumber, Window = _window };
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
    public int FieldCount => Current?.FieldCount ?? 0;
    public object GetValue(int i) => Current!.GetValue(i);
    /// <summary>Validates the current cell with the active reader's schema and cached parsers.</summary>
    public bool IsValid(int i) => (Current ?? throw new InvalidOperationException("No current record is available.")).IsValid(i);
    /// <summary>Validates the named current cell with the active reader's schema and cached parsers.</summary>
    public bool IsValid(string name) => (Current ?? throw new InvalidOperationException("No current record is available.")).IsValid(name);
    public string GetName(int i) => Current!.GetName(i);
    public int GetOrdinal(string name) => Current!.GetOrdinal(name);
    public Type GetFieldType(int i) => Current!.GetFieldType(i);
    public bool IsDBNull(int i) => Current!.IsDBNull(i);
    public object this[int i] => Current![i];
    public object this[string name] => Current![name];
    public int RecordsAffected => -1;
    public int Depth => 0;
    public bool NextResult() => false;
    public DataTable GetSchemaTable() => throw new NotImplementedException();
    public int GetInt32(int i) => Current!.GetInt32(i);
    public string GetString(int i) => Current!.GetString(i);
    public bool GetBoolean(int i) => Current!.GetBoolean(i);
    public long GetInt64(int i) => Current!.GetInt64(i);
    public double GetDouble(int i) => Current!.GetDouble(i);
    public float GetFloat(int i) => Current!.GetFloat(i);
    public Guid GetGuid(int i) => Current!.GetGuid(i);
    public short GetInt16(int i) => Current!.GetInt16(i);
    public byte GetByte(int i) => Current!.GetByte(i);
    public char GetChar(int i) => Current!.GetChar(i);
    public DateTime GetDateTime(int i) => Current!.GetDateTime(i);
    public decimal GetDecimal(int i) => Current!.GetDecimal(i);
    public long GetBytes(int i, long fieldoffset, byte[]? buffer, int bufferoffset, int length)
        => Current!.GetBytes(i, fieldoffset, buffer, bufferoffset, length);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        => Current!.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    public IDataReader GetData(int i) => Current!.GetData(i);
    public string GetDataTypeName(int i) => Current!.GetDataTypeName(i);
    public int GetValues(object[] values) => Current!.GetValues(values);

    public T GetFieldValue<T>(int i) => Current!.GetFieldValue<T>(i);

    #endregion

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
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
