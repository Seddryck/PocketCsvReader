using System.Data;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class JsonAllocationComparison
{
    public const int RecordCount = 50_000;
    [Params(false, true)] public bool Ndjson { get; set; }
    [Params(false, true)] public bool Projection { get; set; }
    [Params(false, true)] public bool EscapedTimestamp { get; set; }
    private byte[] _data = [];
    private Func<Stream, IDataReader> _open = null!;
    private int _requestOrdinal;
    private int _statusOrdinal;
    private int _bytesOrdinal;

    [GlobalSetup]
    public void Setup()
    {
        var options = new ParserOptimizationOptions(BufferSize: 4096, ReadAhead: false, RowCountAtStart: false);
        if (Ndjson)
        {
            var builder = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
                .WithResource(r => r.WithEncoding("utf-8")).WithParserOptimizations(options);
            if (Projection) builder.WithProjection(p => p.Property("request").Property("status").Property("bytes"));
            _open = builder.Build().ToDataReader;
        }
        else
        {
            var builder = new JsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options);
            if (Projection) builder.WithProjection(p => p.Property("request").Property("status").Property("bytes"));
            _open = builder.Build().ToDataReader;
        }
        _requestOrdinal = Projection ? 0 : 4;
        _statusOrdinal = Projection ? 1 : 5;
        _bytesOrdinal = Projection ? 2 : 6;
        var input = new StringBuilder();
        if (!Ndjson) input.Append('[');
        for (int i = 0; i < RecordCount; i++)
        {
            if (!Ndjson && i > 0) input.Append(',');
            var row = Expected(i);
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                host = $"192.0.2.{i % 254 + 1}", identity = "-", user = $"user{i % 100}",
                timestamp = "01/Nov/2026:12:34:56 +0000", request = row.Request, status = row.Status, bytes = row.Bytes
            });
            // The original comparison used System.Text.Json's escaped plus sign.
            if (!EscapedTimestamp) json = json.Replace("\\u002B", "+", StringComparison.Ordinal);
            input.Append(json).Append('\n');
        }
        if (!Ndjson) input.Append(']');
        _data = Encoding.UTF8.GetBytes(input.ToString());
        using var reader = _open(new MemoryStream(_data, writable: false));
        int count = 0;
        while (reader.Read())
        {
            if (count >= RecordCount || Project(reader) != Expected(count))
                throw new InvalidOperationException($"Unexpected projected row {count}.");
            count++;
        }
        if (count != RecordCount) throw new InvalidOperationException($"Read {count} rows.");
    }

    private static (string Request, int Status, long Bytes) Expected(int i)
        => ($"GET /items/{i % 1000}?page={i % 10} HTTP/1.1", i % 10 == 0 ? 404 : 200, 1024L + i);

    private (string Request, int Status, long Bytes) Project(IDataReader reader)
        => (reader.GetString(_requestOrdinal), reader.GetInt32(_statusOrdinal), reader.GetInt64(_bytesOrdinal));

    [Benchmark]
    public long ReadThreeValues()
    {
        using var reader = _open(new MemoryStream(_data, writable: false));
        long checksum = 0;
        while (reader.Read())
        {
            var row = Project(reader);
            checksum += row.Request.Length + row.Status + row.Bytes;
        }
        return checksum;
    }

    public long ReadThreeValuesAndTimestamp()
    {
        if (Projection) throw new InvalidOperationException("Timestamp access requires the full schema.");
        using var reader = _open(new MemoryStream(_data, writable: false));
        long checksum = 0;
        while (reader.Read())
        {
            var row = Project(reader);
            checksum += row.Request.Length + row.Status + row.Bytes + reader.GetString(3).Length;
        }
        return checksum;
    }

    public static void RunMatrix()
    {
        Console.WriteLine("Format,Projection,EscapedTimestamp,Records,InputBytes,WallMeanMs,WallStdDevMs,CpuMeanMs,AllocatedBytes,BytesPerRow");
        using var process = Process.GetCurrentProcess();
        foreach (bool ndjson in new[] { false, true })
        foreach (bool projection in new[] { false, true })
        foreach (bool escaped in new[] { false, true })
        {
            var benchmark = new JsonAllocationComparison { Ndjson = ndjson, Projection = projection, EscapedTimestamp = escaped };
            benchmark.Setup();
            var warmup = Stopwatch.StartNew();
            while (warmup.Elapsed.TotalSeconds < 2) benchmark.ReadThreeValues();
            long expected = benchmark.ReadThreeValues();
            var wall = new double[7];
            var cpu = new double[7];
            var allocated = new double[7];
            for (int sample = 0; sample < wall.Length; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var timer = new Stopwatch();
                var cpuBefore = process.TotalProcessorTime;
                long before = GC.GetAllocatedBytesForCurrentThread();
                int count = 0;
                timer.Start();
                do
                {
                    if (benchmark.ReadThreeValues() != expected) throw new InvalidOperationException("Checksum changed.");
                    count++;
                } while (timer.Elapsed.TotalSeconds < 1);
                timer.Stop();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                var cpuElapsed = process.TotalProcessorTime - cpuBefore;
                wall[sample] = timer.Elapsed.TotalMilliseconds / count;
                cpu[sample] = cpuElapsed.TotalMilliseconds / count;
                allocated[sample] = (double)bytes / count;
            }
            double mean = wall.Average();
            double deviation = Math.Sqrt(wall.Sum(x => Math.Pow(x - mean, 2)) / (wall.Length - 1));
            Console.WriteLine(FormattableString.Invariant($"{(ndjson ? "NDJSON" : "JSON")},{projection},{escaped},{RecordCount},{benchmark._data.Length},{mean:F3},{deviation:F3},{cpu.Average():F3},{allocated.Average():F0},{allocated.Average() / RecordCount:F2}"));
        }
    }
}
