using System.Diagnostics;
using Apache.Arrow;
using PocketCsvReader.Arrow;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

internal static class ParallelCsvBatchBenchmark
{
    private const int DeliveryBatchSize = 64;
    private const int Measurements = 3;

    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("pocketcsv-parallel-");
        var files = Enumerable.Range(0, 4).Select(index => Path.Combine(directory.FullName, $"part-{index}.csv")).ToArray();
        try
        {
            Console.WriteLine("layout,output,workers,ordered,rows_per_second,first_64_rows_ms,allocated_bytes,peak_handoff_storage_bytes");
            RunLayout("balanced", files, [24_000, 24_000, 24_000, 24_000]);
            RunLayout("uneven", files, [78_000, 6_000, 6_000, 6_000]);
        }
        finally
        {
            foreach (var file in files)
                File.Delete(file);
            directory.Delete();
        }
    }

    private static void RunLayout(string layout, string[] files, int[] sizes)
    {
        int id = 0;
        for (int file = 0; file < sizes.Length; file++)
        {
            using var output = new StreamWriter(files[file]);
            output.Write("Id,Name,Metric,Category\n");
            for (int row = 0; row < sizes[file]; row++)
            {
                id++;
                output.Write(FormattableString.Invariant($"{id},\"name {id % 101}, example\",{id % 1_000},group-{id % 11}\n"));
            }
        }

        foreach (var output in Enum.GetValues<Output>())
        {
            var baseline = Measure(files, output, workers: 1, ordered: true);
            foreach (var (workers, ordered) in new[] { (1, true), (2, true), (2, false), (4, true), (4, false) })
            {
                _ = Measure(files, output, workers, ordered);
                var samples = Enumerable.Range(0, Measurements).Select(_ => Measure(files, output, workers, ordered)).ToArray();
                if (samples.Any(sample => sample.Rows != id || sample.Checksum != baseline.Checksum))
                    throw new InvalidDataException("The batch modes produced different output.");

                Console.WriteLine(FormattableString.Invariant(
                    $"{layout},{output},{workers},{ordered},{Median(samples.Select(x => x.RowsPerSecond)):F0},{Median(samples.Select(x => x.FirstBatchMs)):F3},{Median(samples.Select(x => (double)x.AllocatedBytes)):F0},{samples.Max(x => x.PeakStorageBytes)}"));
            }
        }
    }

    private static double Median(IEnumerable<double> values) => values.Order().ElementAt(Measurements / 2);

    private static Sample Measure(string[] files, Output output, int workers, bool ordered)
    {
        var builder = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithSchema(s => s.Indexed().WithField<int>().WithField<string>().WithField<int>().WithField<string>())
            .WithBatchParallelism(workers, ordered);
        if (output != Output.Typed)
            builder.WithProjection("Id", "Name");
        var csv = builder.Build();

        int rows = 0;
        long checksum = 0;
        TimeSpan firstBatch = default;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();
        using var reader = csv.ToDataReader(files);
        if (output == Output.Arrow)
        {
            foreach (var owned in CsvArrowReaderExtensions.Read(() => reader, DeliveryBatchSize))
            {
                using var batch = owned;
                if (rows == 0)
                    firstBatch = elapsed.Elapsed;

                var ids = (Int32Array)batch.Batch.Column(0);
                var names = (StringArray)batch.Batch.Column(1);
                for (var i = 0; i < batch.Batch.Length; i++)
                    checksum += ids.GetValue(i).GetValueOrDefault() + names.GetBytes(i).Length + batch.Sources.Span[i].RecordNumber;
                rows += batch.Batch.Length;
            }
        }
        else
        {
            while (reader.Read())
            {
                if (++rows == DeliveryBatchSize)
                    firstBatch = elapsed.Elapsed;
                checksum += reader.GetInt32(0) + reader.GetString(1).Length + reader.CurrentSource!.Value.RecordNumber;
                if (output == Output.Typed)
                    checksum += reader.GetInt32(2) + reader.GetString(3).Length;
            }
        }

        elapsed.Stop();
        return new(rows, checksum, rows / elapsed.Elapsed.TotalSeconds, firstBatch.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore, reader.PeakBufferedStorage);
    }

    private enum Output
    {
        Typed,
        Projected,
        Arrow
    }
    private readonly record struct Sample(int Rows, long Checksum, double RowsPerSecond, double FirstBatchMs,
        long AllocatedBytes, long PeakStorageBytes);
}
