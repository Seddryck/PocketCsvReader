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
public abstract class BaseDataReader<P> : BaseDataRecord<P>, IAsyncDataReader where P : IProfile
{
    private bool _isClosed = false;
    private bool _initialized;
    protected IRecordSource<P>? RecordSource { get; private set; }
    protected BaseRecordParser<P>? RecordParser => RecordSource as BaseRecordParser<P>;
    private Stream RawStream { get; }
    private Stream? ProcessedStream { get; set; }
    private StreamReader? StreamReader { get; set; }
    protected EncodingInfo? FileEncoding { get; set; }
    protected bool IsEof { get; set; } = false;
    private int _readInProgress;

    protected BaseDataReader(Stream stream, P profile, StringMapper stringMapper)
        : base(profile, stringMapper)
    {
        RawStream = stream;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        if (FileEncoding is null && !string.IsNullOrEmpty(Profile.Resource?.Encoding))
        {
            var encoding = Encoding.GetEncoding(Profile.Resource.Encoding);
            FileEncoding = new(encoding, -1);
        }

        if (!string.IsNullOrEmpty(Profile.Resource?.Compression))
        {
            var factory = ((FileEncoding?.BomBytesCount ?? 0) < 0)
                ? DecompressorFactory.Streaming()
                : DecompressorFactory.Buffered();
            var decompressor = factory.GetDecompressor(Profile.Resource.Compression);
            ProcessedStream = decompressor.Decompress(RawStream);
        }
        else
            ProcessedStream = RawStream;

        FileEncoding ??= Profile.StreamInitialization.ProbeEncoding && ProcessedStream.CanSeek
            ? new EncodingDetector().GetStreamEncoding(ProcessedStream, Profile.Resource?.Encoding)
            : new EncodingInfo(Encoding.UTF8, -1);
        StreamReader = new StreamReader(ProcessedStream, FileEncoding!.Encoding, FileEncoding.BomBytesCount < 0, bufferSize: 1024, leaveOpen: true);
        if (FileEncoding.BomBytesCount >= 0)
        {
            var bufferBOM = new char[1];
            StreamReader.Read(bufferBOM, 0, bufferBOM.Length);
            StreamReader.Rewind();

            if (FileEncoding!.BomBytesCount > 0)
                StreamReader.BaseStream.Position = FileEncoding!.BomBytesCount;
        }

        IsEof = false;
        RowCount = 0;
        RecordSource = CreateRecordSource(StreamReader, Profile);
        _initialized = true;
    }

    protected virtual IRecordSource<P> CreateRecordSource(StreamReader reader, P profile)
        => CreateRecordParser(reader, profile);

    protected virtual BaseRecordParser<P> CreateRecordParser(StreamReader reader, P profile)
        => throw new NotSupportedException("Override CreateRecordSource or CreateRecordParser to provide records.");

    public bool Read()
    {
        EnterRead();
        try
        {
            Initialize();
            return !IsEof && ReadCore();
        }
        finally
        {
            ExitRead();
        }
    }

    protected abstract bool ReadCore();

    public async Task<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return !IsEof && await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitRead();
        }
    }

    protected virtual ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadCore());
    }

    private void EnterRead()
    {
        if (Interlocked.CompareExchange(ref _readInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent Read and ReadAsync calls are not supported.");
    }

    private void ExitRead() => Volatile.Write(ref _readInProgress, 0);

    private async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        if (FileEncoding is null && !string.IsNullOrEmpty(Profile.Resource?.Encoding))
        {
            var encoding = Encoding.GetEncoding(Profile.Resource.Encoding);
            FileEncoding = new(encoding, -1);
        }

        if (!string.IsNullOrEmpty(Profile.Resource?.Compression))
        {
            var factory = ((FileEncoding?.BomBytesCount ?? 0) < 0)
                ? DecompressorFactory.Streaming()
                : DecompressorFactory.Buffered();
            var decompressor = factory.GetDecompressor(Profile.Resource.Compression);
            ProcessedStream = decompressor.Decompress(RawStream);
        }
        else
            ProcessedStream = RawStream;

        FileEncoding ??= Profile.StreamInitialization.ProbeEncoding && ProcessedStream.CanSeek
            ? await new EncodingDetector().GetStreamEncodingAsync(ProcessedStream, Profile.Resource?.Encoding, cancellationToken).ConfigureAwait(false)
            : new EncodingInfo(Encoding.UTF8, -1);

        if (FileEncoding.BomBytesCount >= 0 && ProcessedStream.CanSeek)
            ProcessedStream.Position = FileEncoding.BomBytesCount;

        StreamReader = new StreamReader(ProcessedStream, FileEncoding.Encoding, FileEncoding.BomBytesCount < 0, bufferSize: 1024, leaveOpen: true);
        IsEof = false;
        RowCount = 0;
        RecordSource = CreateRecordSource(StreamReader, Profile);
        _initialized = true;
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
            RecordSource?.Dispose();
            StreamReader?.Dispose();
            ProcessedStream?.Dispose();
            if (ProcessedStream != RawStream)
                RawStream?.Dispose();
        }
    }

    private bool _disposed = false;
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        RecordSource?.Dispose();
        StreamReader?.Dispose();
        if (ProcessedStream is not null && ProcessedStream != RawStream)
            await ProcessedStream.DisposeAsync().ConfigureAwait(false);
        await RawStream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            // free managed resources
            RecordSource?.Dispose();
            StreamReader?.Dispose();
            RawStream?.Dispose();
            ProcessedStream?.Dispose();
        }
    }
    ~BaseDataReader()
    {
        Dispose(false);
    }
}
