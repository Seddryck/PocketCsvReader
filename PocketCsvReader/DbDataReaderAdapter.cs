using System.Collections;
using System.Data;
using System.Data.Common;

namespace PocketCsvReader;

/// <summary>
/// Exposes a PocketCsvReader <see cref="IAsyncDataReader"/> through the standard
/// <see cref="DbDataReader"/> abstraction.
/// </summary>
public sealed class DbDataReaderAdapter : DbDataReader
{
    private readonly IAsyncDataReader _reader;
    private bool? _hasRows;
    private bool _bufferedRecord;
    private int _readInProgress;
    private int _disposed;

    public DbDataReaderAdapter(IAsyncDataReader reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Gets the wrapped PocketCsvReader reader.</summary>
    public IAsyncDataReader InnerReader => _reader;

    public override int Depth => _reader.Depth;
    public override int FieldCount => _reader.FieldCount;
    public override bool IsClosed => Volatile.Read(ref _disposed) != 0 || _reader.IsClosed;
    public override int RecordsAffected => _reader.RecordsAffected;
    public override int VisibleFieldCount => FieldCount;
    public override object this[int ordinal] => _reader[ordinal];
    public override object this[string name] => _reader[name];

    public override bool HasRows
    {
        get
        {
            if (_hasRows.HasValue)
                return _hasRows.Value;

            EnterRead();
            try
            {
                _bufferedRecord = _reader.Read();
                _hasRows = _bufferedRecord;
                return _bufferedRecord;
            }
            finally
            {
                ExitRead();
            }
        }
    }

    public override bool Read()
    {
        EnterRead();
        try
        {
            if (_bufferedRecord)
            {
                _bufferedRecord = false;
                return true;
            }

            var result = _reader.Read();
            _hasRows ??= result;
            return result;
        }
        finally
        {
            ExitRead();
        }
    }

    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        EnterRead();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_bufferedRecord)
            {
                _bufferedRecord = false;
                return true;
            }

            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _hasRows ??= result;
            return result;
        }
        finally
        {
            ExitRead();
        }
    }

    public override bool NextResult() => _reader.NextResult();

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NextResult());
    }

    public override string GetName(int ordinal) => _reader.GetName(ordinal);
    public override string GetDataTypeName(int ordinal) => _reader.GetDataTypeName(ordinal);
    public override Type GetFieldType(int ordinal) => _reader.GetFieldType(ordinal);
    public override object GetValue(int ordinal) => _reader.GetValue(ordinal);
    public override int GetValues(object[] values) => _reader.GetValues(values);
    public override int GetOrdinal(string name) => _reader.GetOrdinal(name);
    public override bool GetBoolean(int ordinal) => _reader.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => _reader.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _reader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => _reader.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _reader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override Guid GetGuid(int ordinal) => _reader.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => _reader.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => _reader.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => _reader.GetInt64(ordinal);
    public override float GetFloat(int ordinal) => _reader.GetFloat(ordinal);
    public override double GetDouble(int ordinal) => _reader.GetDouble(ordinal);
    public override string GetString(int ordinal) => _reader.GetString(ordinal);
    public override decimal GetDecimal(int ordinal) => _reader.GetDecimal(ordinal);
    public override DateTime GetDateTime(int ordinal) => _reader.GetDateTime(ordinal);
    public override bool IsDBNull(int ordinal) => _reader.IsDBNull(ordinal);
    public override DataTable? GetSchemaTable() => _reader.GetSchemaTable();
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override T GetFieldValue<T>(int ordinal)
        => _reader is IFieldValueReader typed
            ? typed.GetFieldValue<T>(ordinal)
            : base.GetFieldValue<T>(ordinal);

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetFieldValue<T>(ordinal));
    }

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsDBNull(ordinal));
    }

    public override void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _reader.Dispose();
    }

    public override async Task CloseAsync()
        => await DisposeAsync().ConfigureAwait(false);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await _reader.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void EnterRead()
    {
        if (Interlocked.CompareExchange(ref _readInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent Read and ReadAsync calls are not supported.");
    }

    private void ExitRead() => Volatile.Write(ref _readInProgress, 0);
}
