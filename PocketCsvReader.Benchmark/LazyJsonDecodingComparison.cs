using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class LazyJsonDecodingComparison
{
    [Params(false, true)]
    public bool Ndjson { get; set; }
    [Params(false, true)]
    public bool AccessTimestamp { get; set; }
    private JsonAllocationComparison _workload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _workload = new JsonAllocationComparison { Ndjson = Ndjson, Projection = false, EscapedTimestamp = true };
        _workload.Setup();
        if (_workload.ReadThreeValuesAndTimestamp() != _workload.ReadThreeValues()
            + (JsonAllocationComparison.RecordCount * "01/Nov/2026:12:34:56 +0000".Length))
            throw new InvalidOperationException("Decoded timestamp checksum differs.");
    }

    [Benchmark]
    public long Read() => AccessTimestamp ? _workload.ReadThreeValuesAndTimestamp() : _workload.ReadThreeValues();

    public static void RunMatrix()
    {
        Console.WriteLine("Format,AccessTimestamp,Records,WallMeanMs,WallStdDevMs,CpuMeanMs,AllocatedBytes,BytesPerRow");
        using var process = Process.GetCurrentProcess();
        foreach (bool ndjson in new[] { false, true })
        {
            foreach (bool access in new[] { false, true })
        {
            var benchmark = new LazyJsonDecodingComparison { Ndjson = ndjson, AccessTimestamp = access };
            benchmark.Setup();
            var warmup = Stopwatch.StartNew();
            while (warmup.Elapsed.TotalSeconds < 2) benchmark.Read();
            long expected = benchmark.Read();
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
                    if (benchmark.Read() != expected) throw new InvalidOperationException("Checksum changed.");
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
            Console.WriteLine(FormattableString.Invariant($"{(ndjson ? "NDJSON" : "JSON")},{access},{JsonAllocationComparison.RecordCount},{mean:F3},{deviation:F3},{cpu.Average():F3},{allocated.Average():F0},{allocated.Average() / JsonAllocationComparison.RecordCount:F2}"));
        }
        }
    }
}
