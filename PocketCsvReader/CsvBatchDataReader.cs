using System;
using System.Collections.Generic;
using System.Data;

namespace PocketCsvReader;
public class CsvBatchDataReader : IDataReader
{
    private Queue<Stream> Streams { get; }
    private CsvProfile Profile { get; }

    private CsvDataReader? Current { get; set; }
    private bool _isClosed = false;

    public CsvBatchDataReader(IEnumerable<Stream> streams, CsvProfile profile)

    {
        (Streams, Profile) = (new(streams), profile);
        MoveNext();
    }

    private void MoveNext()
    {
        Current?.Dispose();

        if (Streams.Count > 0)
        {
            var currentStream = Streams.Dequeue();
            Current = new CsvDataReader(currentStream, Profile);
        }
        else
        {
            Current = null;
        }
    }

    public bool Read()
    {
        if (Current == null)
            return false;

        if (Current.Read())
            return true;

        MoveNext();
        return Read();
    }

    #region composite
    public int FieldCount => Current?.FieldCount ?? 0;
    public object GetValue(int i) => Current!.GetValue(i);
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
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
        => Current!.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
        => Current!.GetChars(i, fieldOffset, buffer, bufferOffset, length);
    public IDataReader GetData(int i) => Current!.GetData(i);
    public string GetDataTypeName(int i) => Current!.GetDataTypeName(i);
    public int GetValues(object[] values) => Current!.GetValues(values);

    #endregion

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            Current?.Dispose();
            while(Streams.Count > 0)
                Streams.Dequeue()?.Dispose();
        }
    }

    public bool IsClosed => _isClosed;

    public void Dispose()
    {
        Close(); // Ensures CsvDataReader is disposed
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvBatchDataReader()
    {
        Dispose();
    }
}
