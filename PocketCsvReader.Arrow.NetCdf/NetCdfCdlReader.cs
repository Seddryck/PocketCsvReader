using System.Text;
using Apache.Arrow.Ipc;

namespace PocketCsvReader.Arrow.NetCdf;

/// <summary>Controls how a CDL dataset is presented as an Arrow table.</summary>
public sealed class NetCdfCdlOptions
{
    /// <value>The row dimensions in slowest-to-fastest order. Null infers an unambiguous shape.</value>
    public IReadOnlyList<string>? RowDimensions { get; init; }

    /// <value>The data variables in output order. Null selects all non-coordinate variables.</value>
    public IReadOnlyList<string>? DataVariables { get; init; }

    /// <value>The maximum number of logical rows in each batch.</value>
    public int BatchSize { get; init; } = 4096;

    /// <value>Use in-memory Arrow run-end encoding for coordinates. Dense arrays are the default.</value>
    public bool RunEndEncodeCoordinates { get; init; }

    /// <value>Leave caller-supplied streams open after staging their input.</value>
    public bool LeaveOpen { get; init; } = true;
}

/// <summary>Reads textual NetCDF CDL from files and streams as Arrow record batches.</summary>
/// <remarks>
/// Opening consumes the CDL document from the input's current position and stages typed cells
/// in a temporary file. The returned stream owns the staging file. Each returned batch has
/// independent storage and must be disposed by the caller.
/// </remarks>
public sealed class NetCdfCdlReader : IArrowStreamSource
{
    private readonly NetCdfCdlOptions _options;

    public NetCdfCdlReader(NetCdfCdlOptions? options = null)
    {
        options ??= new();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BatchSize);
        _options = new()
        {
            BatchSize = options.BatchSize,
            DataVariables = CopyNames(options.DataVariables, nameof(options.DataVariables)),
            RowDimensions = CopyNames(options.RowDimensions, nameof(options.RowDimensions)),
            RunEndEncodeCoordinates = options.RunEndEncodeCoordinates,
            LeaveOpen = options.LeaveOpen,
        };
    }

    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(string filename)
        => Open(OpenFile(filename, false), false, false, default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IArrowArrayStream ToArrowStream(Stream stream)
        => Open(stream, _options.LeaveOpen, false, default).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(string filename, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Open(OpenFile(filename, true), false, true, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IArrowArrayStream> ToArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        => Open(stream, _options.LeaveOpen, true, cancellationToken);

    private static string[]? CopyNames(IReadOnlyList<string>? names, string parameter)
    {
        if (names is null)
            return null;

        var copy = names.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace) || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Selection names must be nonempty and unique.", parameter);
        return copy;
    }

    private static FileStream OpenFile(string filename, bool asynchronous)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
    }

    private async ValueTask<IArrowArrayStream> Open(Stream stream, bool leaveOpen, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var store = new CdlStore(asynchronous);
        try
        {
            CdlDataset dataset;
            try
            {
                token.ThrowIfCancellationRequested();
                using var reader = new StreamReader(stream, new UTF8Encoding(true, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                using var buffer = new SingleBuffer(reader, 4096);
                dataset = await new CdlParser(new CdlLexer(buffer, asynchronous, token), store, token).ParseAsync().ConfigureAwait(false);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The CDL input must be UTF-8 text.", exception);
            }
            finally
            {
                if (!leaveOpen)
                {
                    if (asynchronous)
                        await stream.DisposeAsync().ConfigureAwait(false);
                    else
                        stream.Dispose();
                }
            }

            token.ThrowIfCancellationRequested();
            return CdlProjection.Create(dataset, store, _options);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }
}
