using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class CsvDataReader : IDataReader
{
    private bool _isClosed = false;
    protected RecordParser RecordParser { get; }
    protected Stream Stream { get; }
    protected StreamReader? StreamReader { get; private set; }
    protected Memory<char> buffer;

    protected EncodingInfo? FileEncoding { get; private set; }

    protected bool IsEof { get; private set; } = false;
    public int RowCount { get; private set; } = 0;
    protected int BufferSize { get; private set; } = 64 * 1024;

    public string[]? Fields { get; private set; } = null;
    public string?[]? Values { get; private set; } = null;

    public CsvDataReader(RecordParser recordParser, Stream stream)
    {
        RecordParser = recordParser;
        Stream = stream;
        buffer = new Memory<char>(new char[BufferSize]);
    }

    public void Initialize()
    {
        FileEncoding ??= new EncodingDetector().GetStreamEncoding(Stream);
        StreamReader = new StreamReader(Stream, FileEncoding!.Encoding, false);
        var bufferBOM = new char[1];
        StreamReader.Read(bufferBOM, 0, bufferBOM.Length);
        StreamReader.Rewind();

        if (FileEncoding!.BomBytesCount > 0)
            StreamReader.BaseStream.Position = FileEncoding!.BomBytesCount;

        IsEof = false;
        RowCount = 0;
    }


    public bool Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        (Values, IsEof) = RecordParser.ReadNextRecord(StreamReader, ref buffer);
        if (IsEof && Values!.Length == 0)
        {
            Values = null;
            return false;
        }

        if (RowCount == 0 && Fields is null)
            RegisterHeader(Values!, "field_");

        if (RowCount == 0 && RecordParser.Profile.Descriptor.Header)
        {
            (Values, IsEof) = RecordParser.ReadNextRecord(StreamReader, ref buffer);
            if (IsEof && Values!.Length == 0)
            {
                Values = null;
                return false;
            }
        }
        RowCount++;

        HandleUnexpectedFields(Fields!.Length, Values!);
        HandleMissingFields(Fields!.Length);

        return true;
    }

    protected virtual void RegisterHeader(string?[] names, string prefix)
    {
        int unnamedFieldIndex = 0;
        Fields = (RecordParser.Profile.Descriptor.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{prefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{prefix}{unnamedFieldIndex++}")).ToArray();
    }

    protected virtual void HandleUnexpectedFields(int expectedLength, string?[] values)
    {
        if (expectedLength < values.Length)
            throw new InvalidDataException
            (
                string.Format
                (
                    "The record {0} contains {1} more field{2} than expected."
                    , RowCount + 1 + Convert.ToInt32(RecordParser.Profile.Descriptor.Header)
                    , values.Length - expectedLength
                    , values.Length - expectedLength > 1 ? "s" : string.Empty
                )
            );
    }

    protected virtual void HandleMissingFields(int expectedLength)
    {
        if (RecordParser.Profile.ParserOptimizations.ExtendIncompleteRecords && expectedLength > Values!.Length)
        {
            var missingFields = expectedLength - Values.Length;
            var missingFieldsArray = new string[missingFields];
            Array.Fill(missingFieldsArray,
                RecordParser.Profile.ParserOptimizations.HandleSpecialValues
                    ? RecordParser.Profile.MissingCell
                    : string.Empty);
            Values = Values.Concat(missingFieldsArray).ToArray();
        }
    }

    public object this[int i]
    {
        get
        {
            if (Values is null)
                throw new InvalidOperationException("Values are not defined yet.");
            if (i < 0 || i >= Values.Length)
                throw new IndexOutOfRangeException("Index out of range.");
            return Values[i] ?? throw new InvalidOperationException();
        }
    }

    public object this[string name]
    {
        get
        {
            if (Fields is null)
                throw new InvalidOperationException("Fields are not defined yet.");
            var index = Array.IndexOf(Fields, name);
            if (index < 0)
                throw new InvalidOperationException($"Field '{name}' not found.");
            return Values?[index] ?? throw new InvalidOperationException();
        }
    }

    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => 0;

    public int FieldCount => Fields?.Length ?? throw new InvalidOperationException("Fields are not defined yet.");

    public bool GetBoolean(int i) => throw new NotImplementedException();
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();
    public string GetDataTypeName(int i) => throw new NotImplementedException();
    public DateTime GetDateTime(int i) => throw new NotImplementedException();
    public decimal GetDecimal(int i) => throw new NotImplementedException();
    public double GetDouble(int i) => throw new NotImplementedException();
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i) => throw new NotImplementedException();
    public float GetFloat(int i) => throw new NotImplementedException();
    public Guid GetGuid(int i) => throw new NotImplementedException();
    public short GetInt16(int i) => throw new NotImplementedException();
    public int GetInt32(int i) => throw new NotImplementedException();
    public long GetInt64(int i) => throw new NotImplementedException();
    public string GetName(int i)
        => Fields?[i] ?? throw new InvalidOperationException("Fields are not defined yet.");
    public int GetOrdinal(string name)
    {
        if (Fields is null)
            throw new InvalidOperationException("Fields are not defined yet.");
        var index = Array.IndexOf(Fields, name);
        if (index < 0)
            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        return index;
    }

    public DataTable? GetSchemaTable() => throw new NotImplementedException();

    public string GetString(int i)
        => GetValueOrThrow(i);

    public object GetValue(int i)
        => GetValueOrThrow(i);
    public int GetValues(object[] values) => throw new NotImplementedException();
    public bool IsDBNull(int i)
        => Values![i] is null;
    public bool NextResult() => throw new NotImplementedException();

    private string GetValueOrThrow(int i)
    {
        if (i < (Values?.Length ?? throw new InvalidDataException()))
            return Values[i]!;
        throw new IndexOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Values.Length} defined fields.");
    }

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            StreamReader?.Dispose();
            Stream?.Dispose();
        }
    }

    public void Dispose()
    {
        Close(); // Ensures resources are released
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvDataReader()
    {
        Dispose();
    }
}
