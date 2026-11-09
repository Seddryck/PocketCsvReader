using Apache.Arrow;
using Apache.Arrow.Ipc;

namespace PocketCsvReader.Arrow.ColumnarJson;

/// <summary>Reads ClickHouse JSONColumns and JSONColumnsWithMetadata documents as single Arrow record batches.</summary>
/// <remarks>
/// File streams are owned by the returned Arrow stream. A supplied stream is left open
/// by default. The caller owns each returned batch.
/// </remarks>
public sealed class ClickHouseJsonColumnsReader : IArrowStreamSource, ILazyArrowRecordSource
{
    private readonly Schema? _schema;
    private readonly bool _leaveOpen;

    /// <summary>Configures a reader with the schema of the exported columns.</summary>
    public ClickHouseJsonColumnsReader(Schema schema, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schema = schema;
        _leaveOpen = leaveOpen;
    }

    private ClickHouseJsonColumnsReader(bool leaveOpen) => _leaveOpen = leaveOpen;

    /// <summary>Configures JSONColumnsWithMetadata mode, deriving the Arrow schema from the export's meta array.</summary>
    /// <remarks>The meta array must precede the data object.</remarks>
    public static ClickHouseJsonColumnsReader WithMetadata(bool leaveOpen = true) => new(leaveOpen);

    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(string filename)
        => Open(JsonBatchStream.OpenFile(filename, asynchronous: false), false, false, default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(Stream stream)
        => Open(stream, _leaveOpen, false, default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Open(JsonBatchStream.OpenFile(filename, asynchronous: true), false, true, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        => Open(stream, _leaveOpen, true, cancellationToken);

    /// <summary>Opens a file whose record columns are decoded and cached on first access.</summary>
    public IArrowRecordStream ToLazyArrowStream(string filename)
        => JsonBatchStream.OpenLazyFileAsync(filename, asynchronous: false,
            file => new ColumnsStream(file.Stream, true, false, _schema, file), default).GetAwaiter().GetResult();

    /// <summary>Opens an input stream whose record columns are decoded and cached on first access.</summary>
    public IArrowRecordStream ToLazyArrowStream(Stream stream)
        => new ColumnsStream(stream, _leaveOpen, false, _schema).OpenLazyAsync(default).GetAwaiter().GetResult();

    /// <summary>Asynchronously opens a file for on-demand column decoding.</summary>
    public ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
        => JsonBatchStream.OpenLazyFileAsync(filename, asynchronous: true,
            file => new ColumnsStream(file.Stream, true, true, _schema, file), cancellationToken);

    /// <summary>Asynchronously opens an input stream for on-demand column decoding.</summary>
    public ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        => new ColumnsStream(stream, _leaveOpen, true, _schema).OpenLazyAsync(cancellationToken);

    private ValueTask<IArrowArrayStream> Open(Stream stream, bool leaveOpen, bool asynchronous, CancellationToken cancellationToken)
        => new ColumnsStream(stream, leaveOpen, asynchronous, _schema).OpenAsync(cancellationToken);

    private sealed class ColumnsStream(
        Stream stream, bool leaveOpen, bool asynchronous, Schema? schema, SharedJsonFile? file = null)
        : JsonBatchStream(stream, leaveOpen, asynchronous, schema, file)
    {
        private readonly bool _withMetadata = schema is null;
        private ClickHouseMetadataInput? _metadata;
        private bool _read;

        protected override async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (!_withMetadata)
                return;

            _metadata = new ClickHouseMetadataInput(Input, Scratch);
            Schema = await _metadata.ReadSchemaAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async ValueTask<IArrowRecord?> ReadBatchAsync(bool lazy, CancellationToken cancellationToken)
        {
            if (_read)
                return null;
            var batch = _metadata is null
                ? await ReadDataAsync(lazy, cancellationToken).ConfigureAwait(false)
                : await _metadata.ReadBatchAsync(token => ReadDataAsync(lazy, token), cancellationToken).ConfigureAwait(false);
            try
            {
                if (await Input.PeekSignificantAsync(cancellationToken).ConfigureAwait(false) != -1)
                    throw new InvalidDataException($"Unexpected content after the ClickHouse block at position {Input.Position}.");
                _read = true;
                return batch;
            }
            catch
            {
                batch.Dispose();
                throw;
            }
        }

        private async ValueTask<IArrowRecord> ReadDataAsync(bool lazy, CancellationToken cancellationToken)
        {
            if (!lazy)
                return await ClickHouseBatch.ReadAsync(Input, Schema, Scratch, cancellationToken).ConfigureAwait(false);
            if (SharedFile is { } source)
                return await ClickHouseBatch.ReadLazyFileAsync(Input, Schema, Scratch, source, cancellationToken).ConfigureAwait(false);
            return await ClickHouseBatch.ReadLazyAsync(Input, Schema, Scratch, Asynchronous, cancellationToken).ConfigureAwait(false);
        }
    }
}
