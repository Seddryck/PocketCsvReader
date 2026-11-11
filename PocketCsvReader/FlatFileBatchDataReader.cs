using System.Data;

namespace PocketCsvReader;

internal sealed class FlatFileBatchDataReader(
    Func<CancellationToken, ValueTask<IAsyncDataReader?>> openNext,
    RecordWindow? window,
    bool requiresAsync,
    Action? disposeDiscovery = null) : IAsyncDataReader, IFieldValueReader
{
    private IAsyncDataReader? _current;
    private Func<CancellationToken, ValueTask<IAsyncDataReader?>>? _openNext = openNext;
    private Action? _disposeDiscovery = disposeDiscovery;
    private int _readInProgress;

    public bool Read()
    {
        if (requiresAsync)
            throw new NotSupportedException("This directory source requires ReadAsync.");
        return ReadCoreAsync(useAsync: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<bool> ReadAsync(CancellationToken cancellationToken = default)
        => ReadCoreAsync(useAsync: true, cancellationToken).AsTask();

    private async ValueTask<bool> ReadCoreAsync(bool useAsync, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _readInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent Read and ReadAsync calls are not supported.");

        try
        {
            token.ThrowIfCancellationRequested();
            while (_openNext is { } next && window?.IsComplete != true)
            {
                _current ??= await next(token).ConfigureAwait(false);
                if (_current is null || IsClosed)
                    break;

                if (useAsync ? await _current.ReadAsync(token).ConfigureAwait(false) : _current.Read())
                    return true;

                await DisposeCurrentAsync(useAsync).ConfigureAwait(false);
            }
            await CompleteAsync(useAsync).ConfigureAwait(false);
            return false;
        }
        catch
        {
            IsClosed = true;
            try
            {
                await CompleteAsync(useAsync).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original read or acquisition error.
            }
            throw;
        }
        finally
        {
            Volatile.Write(ref _readInProgress, 0);
        }
    }

    private async ValueTask DisposeCurrentAsync(bool useAsync)
    {
        var current = _current;
        _current = null;
        if (current is null)
            return;

        if (useAsync)
            await current.DisposeAsync().ConfigureAwait(false);
        else
            current.Dispose();
    }

    private async ValueTask CompleteAsync(bool useAsync)
    {
        _openNext = null;
        var dispose = _disposeDiscovery;
        _disposeDiscovery = null;
        try
        {
            await DisposeCurrentAsync(useAsync).ConfigureAwait(false);
        }
        finally
        {
            dispose?.Invoke();
        }
    }

    public void Dispose() => Close();

    public void Close()
    {
        IsClosed = true;
        CompleteAsync(useAsync: false).GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        IsClosed = true;
        return CompleteAsync(useAsync: true);
    }

    private IAsyncDataReader Current => _current ?? throw new InvalidOperationException("No current record is available.");

    public RecordPosition? CurrentPosition => _current?.CurrentPosition;
    public bool IsClosed { get; private set; }
    public int FieldCount => _current?.FieldCount ?? 0;
    public int Depth => 0;
    public int RecordsAffected => -1;
    public bool NextResult() => false;
    public DataTable? GetSchemaTable() => _current?.GetSchemaTable();
    public object this[int i] => Current[i];
    public object this[string name] => Current[name];
    public string GetName(int i) => Current.GetName(i);
    public int GetOrdinal(string name) => Current.GetOrdinal(name);
    public string GetDataTypeName(int i) => Current.GetDataTypeName(i);
    public Type GetFieldType(int i) => Current.GetFieldType(i);
    public object GetValue(int i) => Current.GetValue(i);
    public int GetValues(object[] values) => Current.GetValues(values);
    public bool IsDBNull(int i) => Current.IsDBNull(i);
    public bool GetBoolean(int i) => Current.GetBoolean(i);
    public byte GetByte(int i) => Current.GetByte(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
        => Current.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
    public char GetChar(int i) => Current.GetChar(i);
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
        => Current.GetChars(i, fieldOffset, buffer, bufferOffset, length);
    public IDataReader GetData(int i) => Current.GetData(i);
    public DateTime GetDateTime(int i) => Current.GetDateTime(i);
    public decimal GetDecimal(int i) => Current.GetDecimal(i);
    public double GetDouble(int i) => Current.GetDouble(i);
    public float GetFloat(int i) => Current.GetFloat(i);
    public Guid GetGuid(int i) => Current.GetGuid(i);
    public short GetInt16(int i) => Current.GetInt16(i);
    public int GetInt32(int i) => Current.GetInt32(i);
    public long GetInt64(int i) => Current.GetInt64(i);
    public string GetString(int i) => Current.GetString(i);
    public T GetFieldValue<T>(int i) => ((IFieldValueReader)Current).GetFieldValue<T>(i);
}
