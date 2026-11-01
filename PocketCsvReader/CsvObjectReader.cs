using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class CsvObjectReader<T> : IDisposable
{
    protected RecordParser<T>? RecordParser { get; set; }
    protected CsvProfile Profile { get; }
    protected Stream Stream { get; }
    protected StreamReader? StreamReader { get; private set; }
    private bool LeaveOpen { get; }
    private bool _disposed;
    private CsvDataReader? _dataReader;
    private bool UseDataReader => Profile.StreamInitialization.InputMode != CsvInputMode.DecodedChars
        || Profile.RowNumber is not null
        || !string.IsNullOrEmpty(Profile.Resource?.Encoding);
    protected Memory<char> buffer;
    public int RowCount { get; private set; } = 0;

    protected EncodingInfo? FileEncoding { get; private set; }

    protected bool IsEof { get; private set; } = false;
    protected int BufferSize { get; private set; } = 64 * 1024;

    protected SpanMapper<T> SpanMapper { get; }

    public CsvObjectReader(Stream stream, CsvProfile profile, SpanMapper<T>? spanMapper = null, bool leaveOpen = false)
    {
        Stream = stream;
        buffer = new Memory<char>(new char[BufferSize]);
        Profile = profile;
        SpanMapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);
        LeaveOpen = leaveOpen;
    }

    public void Initialize()
    {
        if (UseDataReader)
        {
            _dataReader ??= new CsvDataReader(Stream, Profile, LeaveOpen);
            _dataReader.Initialize();
            return;
        }
        FileEncoding ??= new EncodingDetector().GetStreamEncoding(Stream);
        StreamReader = new StreamReader(Stream, FileEncoding!.Encoding, false, bufferSize: 1024, leaveOpen: true);
        var bufferBOM = new char[1];
        StreamReader.Read(bufferBOM, 0, bufferBOM.Length);
        StreamReader.Rewind();

        if (FileEncoding!.BomBytesCount > 0)
            StreamReader.BaseStream.Position = FileEncoding!.BomBytesCount;

        IsEof = false;
        RecordParser = new RecordParser<T>(StreamReader, Profile, SpanMapper);
    }

    public IEnumerable<T> Read()
    {
        if (UseDataReader)
        {
            Initialize();
            while (_dataReader!.Read())
            {
                RowCount++;
                yield return _dataReader.MapCurrent(SpanMapper);
            }
            yield break;
        }
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            yield break;

        while (!IsEof)
        {
            if (RowCount == 0 && Profile.Dialect.Header)
            {
                _ = RecordParser!.ReadHeaders();
            }
            IsEof = RecordParser!.IsEndOfFile(out var value, out var state);
            if (state == RecordState.Comment)
                continue;
            if (IsEof && EqualityComparer<T>.Default.Equals(value, default))
                yield break;
            RowCount++;
            yield return value;
        }
        yield break;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RecordParser?.Dispose();
        _dataReader?.Dispose();
        StreamReader?.Dispose();
        if (!LeaveOpen)
            Stream.Dispose();
        _disposed = true;
    }
}
