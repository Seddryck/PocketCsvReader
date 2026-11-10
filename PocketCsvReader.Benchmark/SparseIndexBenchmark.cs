using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

internal static class SparseIndexBenchmark
{
    private const int Rows = 100_000;
    private const int Days = 365;
    private const int Interval = 1024;
    private const int Samples = 31;
    private const string DateFormat = "yyyy-MM-dd";
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("pocket-sparse-bench-");
        try
        {
            var path = Path.Combine(directory.FullName, "orders.csv");
            var first = new DateOnly(2026, 1, 1);
            Generate(path, first);
            var files = new[] { FileSource.Partitioned(path, 2026) };
            var query = Builder().FromFiles(files).Build();
            var index = new SparseIndex(new IndexKey<DateOnly>("Date", format: DateFormat, culture: Culture), Interval);

            query.BuildIndexes(index);
            var build = new double[7];
            for (int i = 0; i < build.Length; i++)
                build[i] = Time(() => query.BuildIndexes(index));

            Console.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"{Rows:N0} rows, {Days} dates, {new FileInfo(path).Length:N0} CSV bytes");
            Console.WriteLine($"Interval {Interval}, {new FileInfo(path + ".sparse.idx").Length:N0} sidecar bytes, build median {Median(build):F3} ms");
            Console.WriteLine($"Warm-cache median of {Samples} samples per lookup. Each opens and consumes a new reader.");
            Console.WriteLine("Date        Rows   Sequential (ms)   Indexed (ms)    Speedup");
            foreach (var date in new[] { first, first.AddDays(Days / 2), first.AddDays(Days - 1) })
            {
                var sequential = Builder()
                    .WithFilter("Date", text => DateOnly.ParseExact(text, DateFormat, Culture) == date)
                    .FromFiles(files).Build();
                var indexed = query.WithIndexedLookup(index, date);
                var expected = ReadMatches(sequential);
                for (int i = 0; i < 8; i++)
                {
                    Check(sequential);
                    Check(indexed);
                }

                var fullTimes = new double[Samples];
                var indexTimes = new double[Samples];
                for (int i = 0; i < Samples; i++)
                {
                    if (i % 2 == 0)
                    {
                        fullTimes[i] = Time(() => Check(sequential));
                        indexTimes[i] = Time(() => Check(indexed));
                    }
                    else
                    {
                        indexTimes[i] = Time(() => Check(indexed));
                        fullTimes[i] = Time(() => Check(sequential));
                    }
                }

                double full = Median(fullTimes), seek = Median(indexTimes);
                Console.WriteLine($"{date:yyyy-MM-dd}  {expected.Count,4}   {full,15:F3}   {seek,12:F3}   {full / seek,7:F1}x");

                void Check(CsvFileQuery<int> candidate)
                {
                    if (ReadMatches(candidate) != expected)
                        throw new InvalidOperationException($"The indexed and sequential lookups disagree for {date:yyyy-MM-dd}.");
                }
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
        .WithStreamInitialization(StreamInitializationOptions.Utf8);

    private static void Generate(string path, DateOnly first)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("Date,OrderId");
        for (int day = 0, row = 0; day < Days; day++)
        {
            var date = first.AddDays(day).ToString(DateFormat, Culture);
            int count = (Rows / Days) + (day < Rows % Days ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                writer.Write(date);
                writer.Write(',');
                writer.WriteLine((++row).ToString(Culture));
            }
        }
    }

    private static (int Count, long Sum) ReadMatches(CsvFileQuery<int> query)
    {
        int count = 0;
        long sum = 0;
        using var reader = query.ToDataReader();
        while (reader.Read())
        {
            count++;
            sum += reader.GetInt32(1);
        }
        return (count, sum);
    }

    private static double Time(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }
}
