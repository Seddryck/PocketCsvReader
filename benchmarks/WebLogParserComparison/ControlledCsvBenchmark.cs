using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PocketCsvReader.Benchmark;

public static class ControlledCsvBenchmark
{
    public static void RunWorker(WebLogParserComparison.LogParser parser, string label,
        int core, int warmupSeconds, int batches, int sampleSeconds)
    {
        if (parser is not (WebLogParserComparison.LogParser.CsvChars or WebLogParserComparison.LogParser.CsvUtf8)
            || core is < 0 or > 62 || warmupSeconds < 1 || batches < 2 || sampleSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(parser), "Use a CSV mode, a valid core, and positive durations.");
        using var process = Process.GetCurrentProcess();
        long affinity = 1L << core;
        if (OperatingSystem.IsWindows())
        {
            process.ProcessorAffinity = (nint)affinity;
            if (process.ProcessorAffinity.ToInt64() != affinity)
                throw new InvalidOperationException("The worker could not pin the requested logical CPU.");
        }
        else
            throw new PlatformNotSupportedException("This controlled worker pins a Windows process.");

        var benchmark = new WebLogParserComparison { Parser = parser };
        benchmark.Setup(); // Checks every value on every one of the 50,000 rows.
        long expected = benchmark.ReadThreeValues();
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed.TotalSeconds < warmupSeconds)
            if (benchmark.ReadThreeValues() != expected) throw new InvalidOperationException("Warmup checksum changed.");

        var samples = new List<object>();
        for (int sample = 0; sample < batches; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var timer = new Stopwatch();
            var cpuBefore = process.TotalProcessorTime;
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            int count = 0;
            timer.Start();
            do
            {
                if (benchmark.ReadThreeValues() != expected) throw new InvalidOperationException("Checksum changed.");
                count++;
            } while (timer.Elapsed.TotalSeconds < sampleSeconds);
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / count;
            samples.Add(new
            {
                Sample = sample + 1, Operations = count,
                WallMs = timer.Elapsed.TotalMilliseconds / count, CpuMs = cpuMs,
                AllocatedBytes = (double)allocated / count,
                Gen0 = GC.CollectionCount(0) - gen0, Gen1 = GC.CollectionCount(1) - gen1, Gen2 = GC.CollectionCount(2) - gen2
            });
        }
        var input = (byte[])typeof(WebLogParserComparison).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(benchmark)!;
        string IlHash(string name) => Convert.ToHexString(SHA256.HashData(typeof(WebLogParserComparison)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetMethodBody()!.GetILAsByteArray()!));
        Console.WriteLine("CONTROLLED_RESULT:" + JsonSerializer.Serialize(new
        {
            Label = label, Parser = parser.ToString(), Core = core, Affinity = $"0x{affinity:X}",
            Framework = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ServerGC = GCSettings.IsServerGC, TieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime-default",
            WarmupSeconds = warmupSeconds, Batches = batches, SampleSeconds = sampleSeconds,
            InputBytes = input.Length, InputHash = Convert.ToHexString(SHA256.HashData(input)),
            ReadIL = IlHash(nameof(WebLogParserComparison.ReadThreeValues)), ProjectionIL = IlHash("Project"),
            ParserAssembly = typeof(CsvReader).Assembly.Location,
            ParserMvid = typeof(CsvReader).Assembly.ManifestModule.ModuleVersionId,
            ExpectedChecksum = expected, Samples = samples
        }));
    }
}
