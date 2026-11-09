using Apache.Arrow;

namespace PocketCsvReader.Arrow;

/// <summary>Reads records whose columns can be materialized on demand.</summary>
/// <remarks>
/// The caller owns each record and disposes it independently of this stream.
/// Arrays returned by a record belong to that record.
/// </remarks>
public interface IArrowRecordStream : IDisposable
{
    /// <summary>Gets the schema before any records are read.</summary>
    /// <value>The schema shared by records from this stream.</value>
    Schema Schema { get; }

    /// <summary>Reads the next record, or returns null at end of input.</summary>
    ValueTask<IArrowRecord?> ReadNextRecordAsync(CancellationToken cancellationToken = default);
}
