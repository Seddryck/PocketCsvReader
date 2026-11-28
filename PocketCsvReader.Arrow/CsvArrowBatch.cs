using Apache.Arrow;

namespace PocketCsvReader.Arrow;

/// <summary>Owns an Arrow batch with the original CSV source of each row.</summary>
public sealed class CsvArrowBatch : IDisposable
{
    internal CsvArrowBatch(RecordBatch batch, ReadOnlyMemory<CsvRowSource> sources)
        => (Batch, Sources) = (batch, sources);

    /// <summary>Gets the Arrow record batch.</summary>
    /// <value>The owned Arrow batch.</value>
    public RecordBatch Batch { get; }

    /// <summary>Gets one source entry for each row in the batch, in row order.</summary>
    /// <value>The source snapshots indexed by Arrow row ordinal.</value>
    public ReadOnlyMemory<CsvRowSource> Sources { get; }

    public void Dispose() => Batch.Dispose();
}
