using Apache.Arrow;
using Apache.Arrow.Ipc;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

/// <summary>Reads Apache Arrow Integration JSON fixtures as Apache Arrow record batches.</summary>
/// <remarks>
/// The fixture schema precedes the batches. File streams are owned by the returned Arrow
/// stream. A supplied stream is left open by default. The caller owns each returned batch.
/// </remarks>
public sealed class ArrowIntegrationJsonReader(bool leaveOpen = true) : IArrowStreamSource, ILazyArrowRecordSource
{
    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(string filename)
        => new FixtureStream(JsonBatchStream.OpenFile(filename, asynchronous: false), false, false)
            .OpenAsync(default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(Stream stream)
        => new FixtureStream(stream, leaveOpen, false).OpenAsync(default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new FixtureStream(JsonBatchStream.OpenFile(filename, asynchronous: true), false, true)
            .OpenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        => new FixtureStream(stream, leaveOpen, true).OpenAsync(cancellationToken);

    /// <summary>Opens a file whose record columns are decoded and cached on first access.</summary>
    public IArrowRecordStream ToLazyArrowStream(string filename)
        => JsonBatchStream.OpenLazyFileAsync(filename, asynchronous: false,
            static file => new FixtureStream(file.Stream, true, false, file), default).GetAwaiter().GetResult();

    /// <summary>Opens an input stream whose record columns are decoded and cached on first access.</summary>
    public IArrowRecordStream ToLazyArrowStream(Stream stream)
        => new FixtureStream(stream, leaveOpen, false).OpenLazyAsync(default).GetAwaiter().GetResult();

    /// <summary>Asynchronously opens a file for on-demand column decoding.</summary>
    public ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
        => JsonBatchStream.OpenLazyFileAsync(filename, asynchronous: true,
            static file => new FixtureStream(file.Stream, true, true, file), cancellationToken);

    /// <summary>Asynchronously opens an input stream for on-demand column decoding.</summary>
    public ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        => new FixtureStream(stream, leaveOpen, true).OpenLazyAsync(cancellationToken);

    private sealed class FixtureStream(Stream stream, bool leaveOpen, bool asynchronous, SharedJsonFile? file = null)
        : JsonBatchStream(stream, leaveOpen, asynchronous, file: file)
    {
        private readonly HashSet<string> _properties = new(StringComparer.Ordinal);
        private bool _insideBatches;
        private bool _hasBatch;

        protected override async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            await Input.ExpectAsync('{', cancellationToken).ConfigureAwait(false);
            if (await Input.TryTakeAsync('}', cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The Arrow fixture is missing a schema.");

            while (true)
            {
                var name = await ReadPropertyNameAsync(cancellationToken).ConfigureAwait(false);
                if (name == "batches")
                    throw new InvalidDataException("The Arrow fixture schema must precede its batches.");
                var value = await Input.ReadValueAsync(cancellationToken).ConfigureAwait(false);
                if (name == "schema")
                {
                    Schema = IntegrationSchema.Parse(value.Span, Scratch);
                    return;
                }

                ValidateExtraProperty(name, value.Span);
                if (!await Input.TryTakeAsync(',', cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("The Arrow fixture is missing a schema.");
            }
        }

        protected override async ValueTask<IArrowRecord?> ReadBatchAsync(bool lazy, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_insideBatches)
                {
                    if (!await Input.TryTakeAsync(']', cancellationToken).ConfigureAwait(false))
                        return await ReadBatchFromArrayAsync(lazy, cancellationToken).ConfigureAwait(false);
                    _insideBatches = false;
                }

                if (await Input.TryTakeAsync('}', cancellationToken).ConfigureAwait(false))
                {
                    if (await Input.PeekSignificantAsync(cancellationToken).ConfigureAwait(false) != -1)
                        throw new InvalidDataException($"Unexpected content after the Arrow fixture at position {Input.Position}.");
                    return null;
                }

                await Input.ExpectAsync(',', cancellationToken).ConfigureAwait(false);
                var name = await ReadPropertyNameAsync(cancellationToken).ConfigureAwait(false);
                if (name == "batches")
                {
                    await Input.ExpectAsync('[', cancellationToken).ConfigureAwait(false);
                    _insideBatches = true;
                }
                else
                {
                    var value = await Input.ReadValueAsync(cancellationToken).ConfigureAwait(false);
                    ValidateExtraProperty(name, value.Span);
                }
            }
        }

        private async ValueTask<IArrowRecord> ReadBatchFromArrayAsync(bool lazy, CancellationToken cancellationToken)
        {
            if (_hasBatch)
                await Input.ExpectAsync(',', cancellationToken).ConfigureAwait(false);
            var batch = await Input.ReadValueAsync(cancellationToken).ConfigureAwait(false);
            _hasBatch = true;
            if (!lazy)
                return IntegrationBatch.Parse(batch.Span, Schema, Scratch, cancellationToken);
            if (SharedFile is { } sourceFile)
                return IntegrationBatch.ReadLazyFile(batch.Span, Schema, Scratch, sourceFile, Input.LastValueByteStart, cancellationToken);
            return await IntegrationBatch.ReadLazyAsync(batch, Schema, Scratch, Asynchronous, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<string> ReadPropertyNameAsync(CancellationToken cancellationToken)
        {
            var name = await Input.ReadPropertyNameAsync(Scratch, cancellationToken).ConfigureAwait(false);
            if (!_properties.Add(name))
                throw new InvalidDataException($"Duplicate Arrow fixture property '{name}'.");
            return name;
        }

        private static void ValidateExtraProperty(string name, ReadOnlySpan<char> value)
        {
            if (name == "dictionaries")
            {
                if (JsonRecordParser.Enumerate(value).MoveNext())
                    throw new NotSupportedException("Arrow fixture dictionaries are unsupported.");
                return;
            }

            JsonRecordParser.ValidateValue(value);
        }
    }
}
