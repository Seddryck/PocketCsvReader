using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
public class PositionTracking
{
    [Params(CsvInputMode.DecodedChars, CsvInputMode.Utf8Bytes)]
    public CsvInputMode Parser { get; set; }
    [Params(false, true)]
    public bool Tracking { get; set; }
    [Params("ShortSkipped", "WideUnicodeSkipped", "MultilineStrings", "Numeric", "WideStrings", "CustomNewlines", "CrossBuffer")]
    public string Scenario { get; set; } = "ShortSkipped";
    private byte[] _data = [];
    private CsvProfile _profile = null!;

    [GlobalSetup]
    public void Setup()
    {
        int columns = Scenario.StartsWith("Wide") ? 24 : 3;
        string value = Scenario.Contains("Unicode") ? "東京é😀"
            : Scenario == "MultilineStrings" ? "\"hello\r\nworld\""
            : Scenario == "CustomNewlines" ? "\"a\r\nb\nc\rd\""
            : Scenario == "CrossBuffer" ? new string('x', 300) : "12345";
        string terminator = Scenario == "CustomNewlines" ? "||" : "\r\n";
        _data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(string.Join(',', Enumerable.Repeat(value, columns)) + terminator, 2000)));
        var options = new ParserOptimizationOptions(BufferSize: Scenario == "CrossBuffer" ? 64 : 4096, ReadAhead: false);
        // Reflection is setup-only so the exact same harness also runs on the pre-feature baseline.
        var setting = typeof(ParserOptimizationOptions).GetProperty("TrackPosition");
        if (Tracking && setting is null) throw new NotSupportedException("Baseline has no tracking implementation.");
        setting?.SetValue(options, Tracking);
        _profile = new(new(Header: false, LineTerminator: terminator))
        {
            StreamInitialization = new(InputMode: Parser), ParserOptimizations = options
        };
    }

    [Benchmark]
    public long Read()
    {
        using var reader = new CsvDataReader(new MemoryStream(_data, writable: false), _profile);
        long sum = 0;
        while (reader.Read())
        {
            if (Scenario.EndsWith("Skipped") || Scenario == "CrossBuffer" || Scenario == "CustomNewlines")
                sum += reader.FieldCount;
            else if (Scenario == "Numeric")
                for (int i = 0; i < reader.FieldCount; i++) sum += reader.GetInt32(i);
            else
                for (int i = 0; i < reader.FieldCount; i++) sum += reader.GetString(i).Length;
        }
        return sum;
    }

    // A repeatable short matrix for comparing the unchanged baseline with both feature modes.
    // BenchmarkDotNet remains available for longer, isolated measurements of individual cases.
    public static void RunMatrix()
    {
        Console.WriteLine("Parser,Scenario,Tracking,MeanUs,StdDevUs,AllocatedBytes,RecordsPerSecond");
        foreach (var parser in new[] { CsvInputMode.DecodedChars, CsvInputMode.Utf8Bytes })
        {
            foreach (string scenario in new[] { "ShortSkipped", "WideUnicodeSkipped", "MultilineStrings", "Numeric", "WideStrings", "CustomNewlines", "CrossBuffer" })
            {
                foreach (bool tracking in typeof(ParserOptimizationOptions).GetProperty("TrackPosition") is null ? new[] { false } : new[] { false, true })
        {
            var benchmark = new PositionTracking { Parser = parser, Scenario = scenario, Tracking = tracking };
            benchmark.Setup();
            var warmup = Stopwatch.StartNew();
            while (warmup.ElapsedMilliseconds < 250) benchmark.Read();
            var samples = new double[7];
            long allocated = 0;
            for (int sample = 0; sample < samples.Length; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                int count = 0;
                do { benchmark.Read(); count++; } while (timer.ElapsedMilliseconds < 150);
                timer.Stop();
                samples[sample] = timer.Elapsed.TotalMicroseconds / count;
                allocated += (GC.GetAllocatedBytesForCurrentThread() - before) / count;
            }
            double mean = samples.Average();
            double deviation = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (samples.Length - 1));
            Console.WriteLine(FormattableString.Invariant($"{parser},{scenario},{tracking},{mean:F2},{deviation:F2},{allocated / samples.Length},{2000 * 1e6 / mean:F0}"));
        }
            }
        }
    }
}
