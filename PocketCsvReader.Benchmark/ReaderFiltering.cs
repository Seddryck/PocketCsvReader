using System.Diagnostics;
using System.Text;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

/// <summary>A short allocation and parser-reuse check; run with --reader-filtering-matrix.</summary>
public static class ReaderFiltering
{
    public static void RunMatrix()
    {
        const int rows = 50000;
        var bytes = Encoding.UTF8.GetBytes("Value\n" + string.Join("\n", Enumerable.Repeat("12345", rows)));
        Console.WriteLine("Mode | Rows | Bytes/row | Milliseconds | Parser calls");
        foreach (var mode in new[] { "none", "span", "string-row", "typed-manual", "typed-configured" })
        {
            Run(mode, bytes); // Warm JIT, parser bindings, and pools before measuring.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var result = Run(mode, bytes);
            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"{mode} | {result.Rows} | {(double)allocated / rows:F2} | {watch.Elapsed.TotalMilliseconds:F2} | {result.Calls}");
            if (result.Rows != rows) throw new InvalidOperationException("Unexpected accepted row count.");
            if (mode == "typed-configured" && result.Calls != rows)
                throw new InvalidOperationException("Configured filtering parsed a projected field twice.");
            if (mode == "typed-manual" && result.Calls != rows * 2)
                throw new InvalidOperationException("Manual comparison did not exercise repeated parsing.");
        }
    }

    private static (int Rows, int Calls) Run(string mode, byte[] bytes)
    {
        var calls = 0;
        var builder = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));
        if (mode == "span") builder.WithFilter(0, value => value.SequenceEqual("12345"));
        if (mode == "string-row") builder.WithRowFilter(row => row.GetFieldValue<string>(0) == "12345");
        if (mode.StartsWith("typed", StringComparison.Ordinal))
        {
            ParseSpan<int> parser = span => { calls++; return int.Parse(span); };
            builder.WithParsers(p => p.WithParser(parser));
            if (mode == "typed-configured") builder.WithFilter<int>(0, value => value > 0);
        }
        using var stream = new MemoryStream(bytes);
        using var reader = builder.Build().ToDataReader(stream);
        var rows = 0;
        while (reader.Read())
        {
            if (mode == "typed-manual" && reader.GetFieldValue<int>(0) <= 0) continue;
            if (mode.StartsWith("typed", StringComparison.Ordinal) && reader.GetFieldValue<int>(0) <= 0)
                throw new InvalidOperationException();
            rows++;
        }
        return (rows, calls);
    }
}
