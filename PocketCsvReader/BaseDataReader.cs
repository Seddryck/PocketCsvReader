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
public abstract class BaseDataReader<TProfile> : BaseDataRecord<TProfile>, IAsyncDataReader
    where TProfile : IProfile
{
    private bool _isClosed = false;
    private bool _initialized;
    private bool _hasCurrentRecord;
    /// <summary>The current record start, or null when tracking is disabled or no record is current.</summary>
    /// <value>The current record start, or null when unavailable.</value>
    public RecordPosition? CurrentPosition => _hasCurrentRecord && !_disposed ? RecordSource?.CurrentPosition : null;
    protected IRecordSource<TProfile>? RecordSource { get; private set; }
    private ICurrentRecordSource? _currentRecordSource;
    protected BaseRecordParser<TProfile>? RecordParser => RecordSource as BaseRecordParser<TProfile>;
    private Stream RawStream { get; }
    private Stream? ProcessedStream { get; set; }
    private StreamReader? StreamReader { get; set; }
    private bool LeaveOpen { get; }
    protected EncodingInfo? FileEncoding { get; set; }
    protected bool IsEof { get; set; } = false;
    private int _readInProgress;

    protected BaseDataReader(Stream stream, TProfile profile, StringMapper stringMapper, bool leaveOpen = false)
        : base(profile, stringMapper)
    {
        RawStream = stream;
        LeaveOpen = leaveOpen;
    }

    public void Initialize()
    {
        if (_initialized) return;
        PrepareStream();
        var source = CreateStreamRecordSource(ProcessedStream!, Profile);
        if (source is null)
        {
            FileEncoding ??= ShouldProbeEncoding
                ? new EncodingDetector().GetStreamEncoding(ProcessedStream!, Profile.Resource?.Encoding)
                : new EncodingInfo(Encoding.UTF8, -1);
            source = CreateCharacterSource();
        }
        CompleteInitialization(source);
    }

    private bool ShouldProbeEncoding => Profile.StreamInitialization.ProbeEncoding && ProcessedStream!.CanSeek;

    private void PrepareStream()
    {
        if (FileEncoding is null && !string.IsNullOrEmpty(Profile.Resource?.Encoding))
            FileEncoding = new(Encoding.GetEncoding(Profile.Resource.Encoding), -1);

        if (!string.IsNullOrEmpty(Profile.Resource?.Compression))
        {
            var factory = (FileEncoding?.BomBytesCount ?? 0) < 0
                ? DecompressorFactory.Streaming()
                : DecompressorFactory.Buffered();
            ProcessedStream = factory.GetDecompressor(Profile.Resource.Compression).Decompress(RawStream);
        }
        else
        {
            ProcessedStream = RawStream;
        }
    }

    private IRecordSource<TProfile> CreateCharacterSource()
    {
        // Let StreamReader consume its matching preamble once. Skipping it here
        // as well could discard a following U+FEFF that is actual field data.
        // A custom BOM length without a matching encoding preamble is skipped explicitly.
        if (FileEncoding!.BomBytesCount >= 0 && ProcessedStream!.CanSeek)
        {
            ProcessedStream.Position = FileEncoding.BomBytesCount == FileEncoding.Encoding.Preamble.Length
                ? 0 : FileEncoding.BomBytesCount;
        }

        StreamReader = new StreamReader(ProcessedStream!, FileEncoding.Encoding,
            FileEncoding.BomBytesCount < 0, bufferSize: 1024, leaveOpen: true);
        return CreateRecordSource(StreamReader, Profile);
    }

    private void CompleteInitialization(IRecordSource<TProfile> source)
    {
        IsEof = false;
        RowCount = 0;
        RecordSource = source;
        // Source capability is fixed for this reader's lifetime. Do the type and
        // eligibility checks once, rather than on every synchronous/async row.
        _currentRecordSource = source is ICurrentRecordSource { SupportsCurrentRecord: true } current ? current : null;
        _initialized = true;
    }

    protected virtual IRecordSource<TProfile> CreateRecordSource(StreamReader reader, TProfile profile)
        => CreateRecordParser(reader, profile);

    protected virtual IRecordSource<TProfile>? CreateStreamRecordSource(Stream stream, TProfile profile) => null;

    protected virtual ValueTask<IRecordSource<TProfile>?> CreateStreamRecordSourceAsync(Stream stream, TProfile profile, CancellationToken cancellationToken)
        => ValueTask.FromResult<IRecordSource<TProfile>?>(null);

    protected virtual BaseRecordParser<TProfile> CreateRecordParser(StreamReader reader, TProfile profile)
        => throw new NotSupportedException("Override CreateRecordSource or CreateRecordParser to provide records.");

    public bool Read()
    {
        EnterRead();
        try
        {
            Initialize();
            _hasCurrentRecord = false;
            return _hasCurrentRecord = !IsEof && ReadCore();
        }
        finally
        {
            ExitRead();
        }
    }

    protected abstract bool ReadCore();

    // Prefer borrowed current-record transport. Public/custom sources retain their
    // existing virtual entry points and ownership behavior through the fallback.
    private protected CurrentRecordResult ReadSource()
    {
        if (_currentRecordSource is { } source)
            return source.ReadCurrent();
        var eof = RecordSource!.IsEndOfFile(out var record, out var state);
        return new(eof, record.Current, state);
    }

    private protected ValueTask<CurrentRecordResult> ReadSourceAsync(CancellationToken cancellationToken)
    {
        if (_currentRecordSource is { } source)
            return source.ReadCurrentAsync(cancellationToken);
        return ReadPublicSourceAsync(cancellationToken);
    }

    private async ValueTask<CurrentRecordResult> ReadPublicSourceAsync(CancellationToken cancellationToken)
    {
        var result = await RecordSource!.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new(result.IsEndOfFile, result.Record.Current, result.State);
    }

    protected override void EnsureValidationRecord()
    {
        if (!_hasCurrentRecord || _isClosed || _disposed)
            throw new InvalidOperationException("No current record is available.");
    }

    public async Task<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnterRead();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            _hasCurrentRecord = false;
            return _hasCurrentRecord = !IsEof && await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
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
        if (_initialized) return;
        PrepareStream();
        var source = await CreateStreamRecordSourceAsync(ProcessedStream!, Profile, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            FileEncoding ??= ShouldProbeEncoding
                ? await new EncodingDetector().GetStreamEncodingAsync(ProcessedStream!, Profile.Resource?.Encoding, cancellationToken).ConfigureAwait(false)
                : new EncodingInfo(Encoding.UTF8, -1);
            source = CreateCharacterSource();
        }
        CompleteInitialization(source);
    }

    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => 0;

    public DataTable? GetSchemaTable() => throw new NotImplementedException();

    public bool NextResult() => throw new NotImplementedException();

    public void Close()
    {
        _hasCurrentRecord = false;
        if (!_isClosed)
        {
            _isClosed = true;
            RecordSource?.Dispose();
            StreamReader?.Dispose();
            if (ProcessedStream is not null && ProcessedStream != RawStream)
                ProcessedStream.Dispose();
            if (!LeaveOpen)
                RawStream.Dispose();
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
        if (!LeaveOpen)
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
            if (ProcessedStream is not null && ProcessedStream != RawStream)
                ProcessedStream.Dispose();
            if (!LeaveOpen)
                RawStream.Dispose();
        }
    }
    ~BaseDataReader()
    {
        Dispose(false);
    }
}
