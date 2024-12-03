using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PocketCsvReader;
public class CsvDataReader : IDataReader
{
    private bool _isClosed = false;
    private RecordParser? RecordParser { get; set; }
    private CsvProfile Profile { get; }
    private Stream Stream { get; }
    private StreamReader? StreamReader { get; set; }
    private Memory<char> Buffer { get; set; }
    private StringMapper StringMapper { get; }
    private EncodingInfo? FileEncoding { get; set; }

    private bool IsEof { get; set; } = false;
    public int RowCount { get; private set; } = 0;
    private int BufferSize { get; set; } = 64 * 1024;

    public string[]? Fields { get; private set; } = null;
    public RecordMemory? Record { get; private set; } = null;

    public CsvDataReader(Stream stream, CsvProfile profile)
    {
        Stream = stream;
        Buffer = new Memory<char>(new char[BufferSize]);
        Profile = profile;
        StringMapper = new StringMapper(Profile);
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
        RecordParser = new RecordParser(StreamReader, Profile);
    }

    public bool Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            return false;

        if (RowCount == 0)
            if (RecordParser!.Profile.Descriptor.Header)
                RegisterHeader(RecordParser!.ReadHeaders(), "field_");

        IsEof = RecordParser!.ReadNextRecord(out RecordSpan rawRecord);
        if (RowCount == 0 && !RecordParser!.Profile.Descriptor.Header)
            RegisterHeader((string?[])Array.CreateInstance(typeof(string), rawRecord.FieldSpans.Length), "field_");

        if (rawRecord.FieldSpans.Length == 0)
        {
            Record = RecordMemory.Empty;
            return false;
        }
        else
            Record = rawRecord.AsMemory();

        RowCount++;

        HandleUnexpectedFields(Fields!.Length);

        return true;
    }

    private void RegisterHeader(string?[] names, string prefix)
    {
        int unnamedFieldIndex = 0;
        Fields = (RecordParser!.Profile.Descriptor.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{prefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{prefix}{unnamedFieldIndex++}")).ToArray();
    }

    private void HandleUnexpectedFields(int expectedLength)
    {
        var length = Record!.FieldSpans.Length;
        if (expectedLength < length)
            throw new InvalidDataException
            (
                string.Format
                (
                    "The record {0} contains {1} more field{2} than expected."
                    , RowCount + 1 + Convert.ToInt32(RecordParser!.Profile.Descriptor.Header)
                    , length - expectedLength
                    , length - expectedLength > 1 ? "s" : string.Empty
                )
            );
    }

    public object this[int i]
    {
        get
        {
            if (i < Record!.FieldSpans.Length && Fields!.Length > 0)
                return Record.Slice(i).ToString();
            if (i < Fields!.Length)
                return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;
            if (Fields!.Length == 0)
                throw new InvalidOperationException("Values are not defined yet.");
            throw new IndexOutOfRangeException("Index out of range.");
        }
    }

    public object this[string name]
    {
        get
        {
            if (Fields is null)
                throw new InvalidOperationException("Fields are not defined yet.");
            var index = Array.IndexOf(Fields, name);
            if (index < Record!.FieldSpans.Length)
                return Record.Slice(index).ToString();
            if (index < Fields!.Length)
                return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;
            throw new InvalidOperationException($"Field '{name}' not found.");
        }
    }

    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => 0;

    public int FieldCount => Fields?.Length ?? throw new InvalidOperationException("Fields are not defined yet.");

    public bool GetBoolean(int i) => bool.Parse(GetValueOrThrow(i));
    public byte GetByte(int i) => throw new NotImplementedException();
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => throw new NotImplementedException();
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public IDataReader GetData(int i) => throw new NotImplementedException();
    public string GetDataTypeName(int i) => throw new NotImplementedException();
    public DateTime GetDateTime(int i) => DateTime.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public decimal GetDecimal(int i) => decimal.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public double GetDouble(int i) => double.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type GetFieldType(int i) => throw new NotImplementedException();
    public float GetFloat(int i) => float.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public Guid GetGuid(int i) => throw new NotImplementedException();
    public short GetInt16(int i) => short.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public int GetInt32(int i) => int.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
    public long GetInt64(int i) => long.Parse(GetValueOrThrow(i), CultureInfo.InvariantCulture);
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
        => StringMapper.Parse(GetValueOrThrow(i)
                , i < Record!.FieldSpans.Length && Record!.FieldSpans[i].IsEscaped
                , i < Record!.FieldSpans.Length && Record!.FieldSpans[i].WasQuoted)!;
    public object GetValue(int i)
        => GetValueOrThrow(i).ToString();
    public int GetValues(object[] values) => throw new NotImplementedException();
    public bool IsDBNull(int i)
        => StringMapper.Parse(GetValueOrThrow(i), Record!.FieldSpans[i].IsEscaped, Record!.FieldSpans[i].WasQuoted) is null;

    public bool NextResult() => throw new NotImplementedException();

    private ReadOnlySpan<char> GetValueOrThrow(int i)
    {
        if (i < Record!.FieldSpans.Length)
            return Record.Slice(i).Span;
        if (i < Fields!.Length && Profile.ParserOptimizations.ExtendIncompleteRecords)
            return Profile.ParserOptimizations.HandleSpecialValues ? Profile.MissingCell : string.Empty;
        throw new IndexOutOfRangeException($"Attempted to access field index '{i}' in record '{RowCount}', but this row only contains {Record.FieldSpans.Length} defined fields.");
    }

    public void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            StreamReader?.Dispose();
            Stream?.Dispose();
            RecordParser?.Dispose();
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
