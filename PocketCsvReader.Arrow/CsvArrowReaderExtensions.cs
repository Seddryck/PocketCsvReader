using System.Runtime.CompilerServices;

namespace PocketCsvReader.Arrow;

/// <summary>Converts multiple CSV inputs into Arrow batches using the CSV reader's batch configuration.</summary>
public static class CsvArrowReaderExtensions
{
    public static IEnumerable<CsvArrowBatch> ToArrowBatches(this CsvReader source, string[] filenames,
        int batchSize = 65_536)
        => Read(() => source.ToDataReader(filenames), batchSize);

    public static IEnumerable<CsvArrowBatch> ToArrowBatches(this CsvReader source, IEnumerable<Stream> streams,
        int batchSize = 65_536)
        => Read(() => source.ToDataReader(streams), batchSize);

    public static IEnumerable<CsvArrowBatch> ToArrowBatches(this CsvReader source, IEnumerable<Func<Stream>> openers,
        int batchSize = 65_536)
        => Read(() => source.ToDataReader(openers), batchSize);

    public static IAsyncEnumerable<CsvArrowBatch> ToArrowBatchesAsync(this CsvReader source, string[] filenames,
        int batchSize = 65_536, CancellationToken cancellationToken = default)
        => ReadAsync(() => source.ToDataReader(filenames), batchSize, cancellationToken);

    public static IAsyncEnumerable<CsvArrowBatch> ToArrowBatchesAsync(this CsvReader source, IEnumerable<Stream> streams,
        int batchSize = 65_536, CancellationToken cancellationToken = default)
        => ReadAsync(() => source.ToDataReader(streams), batchSize, cancellationToken);

    public static IAsyncEnumerable<CsvArrowBatch> ToArrowBatchesAsync(this CsvReader source, IEnumerable<Func<Stream>> openers,
        int batchSize = 65_536, CancellationToken cancellationToken = default)
        => ReadAsync(() => source.ToDataReader(openers), batchSize, cancellationToken);

    public static IAsyncEnumerable<CsvArrowBatch> ToArrowBatchesAsync(this CsvReader source,
        IEnumerable<Func<CancellationToken, ValueTask<Stream>>> openers,
        int batchSize = 65_536, CancellationToken cancellationToken = default)
        => ReadAsync(() => source.ToDataReader(openers), batchSize, cancellationToken);

    internal static IEnumerable<CsvArrowBatch> Read(Func<CsvBatchDataReader> open, int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        using var reader = open();
        CsvArrowBatchBuilder? builder = null;
        while (reader.Read())
        {
            builder ??= new(reader.CurrentCsvRow!);
            builder.Append(reader);
            if (builder.Count == batchSize)
                yield return builder.Flush();
        }

        if (builder is { Count: > 0 })
            yield return builder.Flush();
    }

    private static async IAsyncEnumerable<CsvArrowBatch> ReadAsync(Func<CsvBatchDataReader> open, int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        cancellationToken.ThrowIfCancellationRequested();
        await using var reader = open();
        CsvArrowBatchBuilder? builder = null;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder ??= new(reader.CurrentCsvRow!);
            builder.Append(reader);
            if (builder.Count == batchSize)
                yield return builder.Flush();
        }

        if (builder is { Count: > 0 })
            yield return builder.Flush();
    }

    private sealed class CsvArrowBatchBuilder(CsvDataReader row)
    {
        private readonly ArrowBatchLayout _layout = ArrowBatchLayout.Create(row);
        private readonly List<CsvRowSource> _sources = [];
        private ArrowBatchBuilder? _batch;

        public int Count => _sources.Count;

        public void Append(CsvBatchDataReader reader)
        {
            _batch ??= _layout.CreateBatch();
            _batch.Append(reader.CurrentCsvRow!);
            _sources.Add(reader.CurrentSource!.Value);
        }

        public CsvArrowBatch Flush()
        {
            var sources = _sources.ToArray();
            var batch = _batch!.Build();
            _batch = null;
            _sources.Clear();
            return new(batch, sources);
        }
    }
}
