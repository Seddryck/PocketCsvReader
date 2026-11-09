using Apache.Arrow;
using Apache.Arrow.Ipc;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal abstract class JsonBatchStream : IArrowArrayStream, IArrowRecordStream
{
    private bool _disposed;
    private bool _finished;
    private Schema? _schema;

    protected BufferedJsonInput Input { get; }
    protected bool Asynchronous { get; }
    protected SharedJsonFile? SharedFile { get; }
    protected JsonFieldBuffer Scratch { get; } = new();

    public Schema Schema
    {
        get => _schema ?? throw new InvalidOperationException("The Arrow stream has not been initialized.");
        protected set => _schema = value;
    }

    protected JsonBatchStream(
        Stream stream, bool leaveOpen, bool asynchronous, Schema? schema = null, SharedJsonFile? file = null)
    {
        Input = new BufferedJsonInput(stream, leaveOpen, asynchronous, file?.Encoding, file?.StartOffset);
        SharedFile = file;
        Asynchronous = asynchronous;
        _schema = schema;
    }

    public static FileStream OpenFile(string filename, bool asynchronous)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        var options = FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, options);
    }

    public static async ValueTask<IArrowRecordStream> OpenLazyFileAsync(
        string filename, bool asynchronous, Func<SharedJsonFile, JsonBatchStream> create, CancellationToken cancellationToken)
    {
        var file = await SharedJsonFile.OpenAsync(filename, asynchronous, cancellationToken).ConfigureAwait(false);
        try
        {
            return await create(file).OpenLazyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public async ValueTask<IArrowArrayStream> OpenAsync(CancellationToken cancellationToken)
    {
        await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        return this;
    }

    public async ValueTask<IArrowRecordStream> OpenLazyAsync(CancellationToken cancellationToken)
    {
        await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        return this;
    }

    private async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async ValueTask<RecordBatch?> ReadNextRecordBatchAsync(CancellationToken cancellationToken = default)
        => (RecordBatch?)await ReadNextAsync(lazy: false, cancellationToken).ConfigureAwait(false);

    public ValueTask<IArrowRecord?> ReadNextRecordAsync(CancellationToken cancellationToken = default)
        => ReadNextAsync(lazy: true, cancellationToken);

    private async ValueTask<IArrowRecord?> ReadNextAsync(bool lazy, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_finished)
            return null;

        try
        {
            var batch = await ReadBatchAsync(lazy, cancellationToken).ConfigureAwait(false);
            _finished = batch is null;
            return batch;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected virtual ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    protected abstract ValueTask<IArrowRecord?> ReadBatchAsync(bool lazy, CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;
        _disposed = true;
        try
        {
            Input.Dispose();
        }
        finally
        {
            SharedFile?.Dispose();
        }
    }
}
