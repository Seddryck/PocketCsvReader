using System.Runtime.CompilerServices;
using Apache.Arrow;
using Apache.Arrow.Ipc;

namespace PocketCsvReader.Arrow;

/// <summary>Enumerates record batches from configured Arrow stream sources.</summary>
public static class ArrowStreamExtensions
{
    /// <summary>Enumerates batches from a file. The caller owns each yielded batch.</summary>
    /// <remarks>Blocks the calling thread. Readers that resume on its synchronization context can deadlock.</remarks>
    public static IEnumerable<RecordBatch> ToArrowBatches(this IArrowStreamSource source, string filename)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        return Enumerate(() => source.ToArrowStream(filename));
    }

    /// <summary>Enumerates batches from a stream. The caller owns each yielded batch.</summary>
    /// <remarks>Blocks the calling thread. Readers that resume on its synchronization context can deadlock.</remarks>
    public static IEnumerable<RecordBatch> ToArrowBatches(this IArrowStreamSource source, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stream);
        return Enumerate(() => source.ToArrowStream(stream));
    }

    /// <summary>Asynchronously enumerates batches from a file. The caller owns each yielded batch.</summary>
    public static IAsyncEnumerable<RecordBatch> ToArrowBatchesAsync(
        this IArrowStreamSource source, string filename, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        return EnumerateAsync(token => source.ToArrowStreamAsync(filename, token), cancellationToken);
    }

    /// <summary>Asynchronously enumerates batches from a stream. The caller owns each yielded batch.</summary>
    public static IAsyncEnumerable<RecordBatch> ToArrowBatchesAsync(
        this IArrowStreamSource source, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stream);
        return EnumerateAsync(token => source.ToArrowStreamAsync(stream, token), cancellationToken);
    }

    private static IEnumerable<RecordBatch> Enumerate(Func<IArrowArrayStream> open)
    {
        using var stream = open();
        while (true)
        {
            var batch = stream.ReadNextRecordBatchAsync().AsTask().GetAwaiter().GetResult();
            if (batch is null)
                yield break;
            yield return batch;
        }
    }

    private static async IAsyncEnumerable<RecordBatch> EnumerateAsync(
        Func<CancellationToken, ValueTask<IArrowArrayStream>> open,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await open(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await stream.ReadNextRecordBatchAsync(cancellationToken).ConfigureAwait(false);
            if (batch is null)
                yield break;
            yield return batch;
        }
    }
}
