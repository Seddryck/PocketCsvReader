using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using PocketCsvReader.Configuration;
using System.Reflection;
using System.Xml.Linq;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader;
public class CsvDataReader : CsvDataRecord, IDataReader
{
    private bool _isClosed = false;
    private RecordParser? RecordParser { get; set; }
    private Stream Stream { get; }
    private StreamReader? StreamReader { get; set; }
    private Memory<char> Buffer { get; set; }
    private EncodingInfo? FileEncoding { get; set; }
    private bool IsEof { get; set; } = false;
    private int BufferSize { get; set; } = 64 * 1024;

    public CsvDataReader(Stream stream, CsvProfile profile)
        : base(profile)
    {
        Stream = stream;
        Buffer = new Memory<char>(new char[BufferSize]);
    }

    public void Initialize()
    {
        FileEncoding ??= new EncodingDetector().GetStreamEncoding(Stream, Profile.Resource?.Encoding);
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
            if (RecordParser!.Profile.Dialect.Header)
                RegisterHeader(RecordParser!.ReadHeaders(), "field_");

        IsEof = RecordParser!.ReadNextRecord(out RecordSpan rawRecord);
        if (RowCount == 0 && !RecordParser!.Profile.Dialect.Header)
            RegisterHeader([(string?[])Array.CreateInstance(typeof(string), rawRecord.FieldSpans.Length)], "field_");

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

    private void RegisterHeader(string?[][] headers, string unamedPrefix)
    {
        var maxField = headers.Select(x => x.Length).Max();
        var names = (string[])Array.CreateInstance(typeof(string), maxField);

        foreach (var header in headers)
        {
            var last = string.Empty;
            for (int i = 0; i < maxField; i++)
            {
                if (i < header.Length && !string.IsNullOrEmpty(header[i]))
                    last = header[i];
                names[i] = string.IsNullOrEmpty(names[i])
                            ? $"{last}"
                            : $"{names[i]}{Profile.Dialect.HeaderJoin}{last}";
            }
        }
        int unnamedFieldIndex = 0;
        Fields = (RecordParser!.Profile.Dialect.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{unamedPrefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{unamedPrefix}{unnamedFieldIndex++}")).ToArray();
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
                    , RowCount + 1 + Convert.ToInt32(RecordParser!.Profile.Dialect.Header)
                    , length - expectedLength
                    , length - expectedLength > 1 ? "s" : string.Empty
                )
            );
    }

    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => 0;

    public DataTable? GetSchemaTable() => throw new NotImplementedException();

    public bool NextResult() => throw new NotImplementedException();

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
        Close(); // Ensures resSequences are released
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvDataReader()
    {
        Dispose();
    }
}
