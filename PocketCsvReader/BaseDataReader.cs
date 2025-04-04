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
public abstract class BaseDataReader<P> : BaseDataRecord<P>, IDataReader where P : IProfile
{
    private bool _isClosed = false;
    protected BaseRecordParser<P>? RecordParser { get; private set; }
    private Stream Stream { get; }
    private StreamReader? StreamReader { get; set; }
    protected EncodingInfo? FileEncoding { get; set; }
    protected bool IsEof { get; set; } = false;

    protected BaseDataReader(Stream stream, P profile, StringMapper stringMapper)
        : base(profile, stringMapper)
    {
        Stream = stream;
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
        RecordParser = CreateRecordParser(StreamReader, Profile);
    }

    protected abstract BaseRecordParser<P> CreateRecordParser(StreamReader reader, P profile);

    public abstract bool Read();

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

    private bool _disposed = false;
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            // free managed resources
            StreamReader?.Dispose();
            Stream?.Dispose();
            RecordParser?.Dispose();
        }
    }
    ~BaseDataReader()
    {
        Dispose(false);
    }
}
