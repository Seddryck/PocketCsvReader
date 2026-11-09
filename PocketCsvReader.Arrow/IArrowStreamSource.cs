using Apache.Arrow.Ipc;

namespace PocketCsvReader.Arrow;

/// <summary>Opens independent Apache Arrow streams from files and streams.</summary>
/// <remarks>
/// The caller disposes each returned Arrow stream and each batch it reads. Implementations
/// define ownership of a supplied input stream. Asynchronous opening uses asynchronous I/O
/// for initialization, including schema reads, and releases acquired resources on failure.
/// </remarks>
public interface IArrowStreamSource
{
    /// <summary>Opens an Arrow stream from a file.</summary>
    IArrowArrayStream ToArrowStream(string filename);

    /// <summary>Opens an Arrow stream from an input stream.</summary>
    IArrowArrayStream ToArrowStream(Stream stream);

    /// <summary>Asynchronously opens an Arrow stream from a file.</summary>
    ValueTask<IArrowArrayStream> ToArrowStreamAsync(string filename, CancellationToken cancellationToken = default);

    /// <summary>Asynchronously opens an Arrow stream from an input stream.</summary>
    ValueTask<IArrowArrayStream> ToArrowStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
