using System.Data;
using System.Data.Common;

namespace PocketCsvReader.Indexing;

internal sealed class ScanningDataReader(IDataReader reader, Func<IDataRecord, bool> matches)
    : IAsyncDataReader, IFieldValueReader
{
    private readonly IAsyncDataReader? _asyncReader = reader as IAsyncDataReader;
    private readonly DbDataReader? _dbReader = reader as DbDataReader;

    public bool Read()
    {
        while (reader.Read())
        {
            if (matches(reader))
                return true;
        }
        return false;
    }

    public async Task<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool read = _asyncReader is not null
                ? await _asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false)
                : _dbReader is not null
                    ? await _dbReader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    : reader.Read();
            if (!read || matches(reader))
                return read;
        }
    }

    public bool NextResult() => reader.NextResult();
    public void Close() => reader.Close();
    public void Dispose() => reader.Dispose();
    public ValueTask DisposeAsync() => reader is IAsyncDisposable asyncDisposable
        ? asyncDisposable.DisposeAsync() : DisposeSynchronously();

    private ValueTask DisposeSynchronously()
    {
        reader.Dispose();
        return ValueTask.CompletedTask;
    }

    public RecordPosition? CurrentPosition => _asyncReader?.CurrentPosition;
    public int Depth => reader.Depth;
    public int FieldCount => reader.FieldCount;
    public bool IsClosed => reader.IsClosed;
    public int RecordsAffected => reader.RecordsAffected;
    public object this[int ordinal] => reader[ordinal];
    public object this[string name] => reader[name];
    public DataTable? GetSchemaTable() => reader.GetSchemaTable();
    public string GetName(int ordinal) => reader.GetName(ordinal);
    public string GetDataTypeName(int ordinal) => reader.GetDataTypeName(ordinal);
    public Type GetFieldType(int ordinal) => reader.GetFieldType(ordinal);
    public object GetValue(int ordinal) => reader.GetValue(ordinal);
    public int GetValues(object[] values) => reader.GetValues(values);
    public int GetOrdinal(string name) => reader.GetOrdinal(name);
    public bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
    public byte GetByte(int ordinal) => reader.GetByte(ordinal);
    public long GetBytes(int ordinal, long offset, byte[]? buffer, int start, int length)
        => reader.GetBytes(ordinal, offset, buffer, start, length);
    public char GetChar(int ordinal) => reader.GetChar(ordinal);
    public long GetChars(int ordinal, long offset, char[]? buffer, int start, int length)
        => reader.GetChars(ordinal, offset, buffer, start, length);
    public Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
    public short GetInt16(int ordinal) => reader.GetInt16(ordinal);
    public int GetInt32(int ordinal) => reader.GetInt32(ordinal);
    public long GetInt64(int ordinal) => reader.GetInt64(ordinal);
    public float GetFloat(int ordinal) => reader.GetFloat(ordinal);
    public double GetDouble(int ordinal) => reader.GetDouble(ordinal);
    public string GetString(int ordinal) => reader.GetString(ordinal);
    public decimal GetDecimal(int ordinal) => reader.GetDecimal(ordinal);
    public DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
    public bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
    public IDataReader GetData(int ordinal) => reader.GetData(ordinal);

    public T GetFieldValue<T>(int ordinal) => reader switch
    {
        IFieldValueReader typed => typed.GetFieldValue<T>(ordinal),
        DbDataReader db => db.GetFieldValue<T>(ordinal),
        _ => (T)reader.GetValue(ordinal)
    };
}
