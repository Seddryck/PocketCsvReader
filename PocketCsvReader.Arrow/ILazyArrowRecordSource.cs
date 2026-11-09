namespace PocketCsvReader.Arrow;

/// <summary>Opens independent Arrow record streams that decode columns on demand.</summary>
/// <remarks>
/// The caller owns each opened stream and every record it returns. Implementations
/// define ownership of a supplied input stream.
/// </remarks>
public interface ILazyArrowRecordSource
{
    /// <summary>Opens a file for on-demand column decoding.</summary>
    IArrowRecordStream ToLazyArrowStream(string filename);

    /// <summary>Opens an input stream for on-demand column decoding.</summary>
    IArrowRecordStream ToLazyArrowStream(Stream stream);

    /// <summary>Asynchronously opens a file for on-demand column decoding.</summary>
    ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(string filename, CancellationToken cancellationToken = default);

    /// <summary>Asynchronously opens an input stream for on-demand column decoding.</summary>
    ValueTask<IArrowRecordStream> ToLazyArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
