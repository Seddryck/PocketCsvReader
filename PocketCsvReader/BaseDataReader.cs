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
using PocketCsvReader.Compression;

namespace PocketCsvReader;
public abstract class BaseDataReader<P> : BaseDataRecord<P>, IDataReader where P : IProfile
{
    private bool _isClosed = false;
    protected BaseRecordParser<P>? RecordParser { get; private set; }
    private Stream RawStream { get; }
    private Stream? ProcessedStream { get; set; }
    private StreamReader? StreamReader { get; set; }
    protected EncodingInfo? FileEncoding { get; set; }
    protected bool IsEof { get; set; } = false;

    protected BaseDataReader(Stream stream, P profile, StringMapper stringMapper)
        : base(profile, stringMapper)
    {
        RawStream = stream;
    }

    public void Initialize()
    {
        if (!string.IsNullOrEmpty(Profile.Resource?.Compression))
        {
            var decompressor = new DecompressorFactory().GetDecompressor(Profile.Resource.Compression);
            ProcessedStream = decompressor.Decompress(RawStream);
        }
        else
            ProcessedStream = RawStream;

        FileEncoding ??= new EncodingDetector().GetStreamEncoding(ProcessedStream, Profile.Resource?.Encoding);
        StreamReader = new StreamReader(ProcessedStream, FileEncoding!.Encoding, false);
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
            ProcessedStream?.Dispose();
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
            RawStream?.Dispose();
            ProcessedStream?.Dispose();
            RecordParser?.Dispose();
        }
    }
    ~BaseDataReader()
    {
        Dispose(false);
    }
}
