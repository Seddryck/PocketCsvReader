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
    protected Memory<char> buffer;
    public int RowCount { get; private set; } = 0;

    protected EncodingInfo? FileEncoding { get; private set; }

    protected bool IsEof { get; private set; } = false;
    protected int BufferSize { get; private set; } = 64 * 1024;

    protected SpanMapper<T> SpanMapper { get; }

    public CsvObjectReader(Stream stream, CsvProfile profile, SpanMapper<T>? spanMapper = null)
    {
        Stream = stream;
        buffer = new Memory<char>(new char[BufferSize]);
        Profile = profile;
        SpanMapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);
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
        RecordParser = new RecordParser<T>(StreamReader, Profile, SpanMapper);
    }

    public IEnumerable<T> Read()
    {
        if (FileEncoding is null)
            Initialize();
        if (IsEof)
            yield break;

        while (!IsEof)
        {
            if (RowCount == 0 && Profile.Dialect.Header)
            {
                var _ = RecordParser!.ReadHeaders();
            }
            IsEof = RecordParser!.ReadNextRecord(out var value);
            if (IsEof && EqualityComparer<T>.Default.Equals(value, default))
                yield break;
            RowCount++;
            yield return value;
        }
        yield break;
    }

    public void Dispose()
    {
        StreamReader?.Dispose();
        Stream?.Dispose();
        RecordParser?.Dispose();
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvObjectReader()
    {
        Dispose();
    }
}
