using System.Diagnostics;
using System.Text;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.Benchmark;

// The same harness runs on the baseline and implementation; generation is outside measurement.
public static class KeyValueBuffering
{
    private const int RecordCount = 512;

    public static void RunMatrix()
    {
        Console.WriteLine("Format,Scenario,MeanUs,StdDevUs,AllocatedBytes,Checksum");
        foreach (bool logfmt in new[] { false, true })
        foreach (string scenario in new[] { "Simple", "Escaped", "CrossBuffer" })
            Measure(new Workload(logfmt, scenario));
    }

    private static void Measure(Workload workload)
    {
        if (workload.Read() != workload.Expected)
            throw new InvalidOperationException("Incorrect benchmark values.");
        var warmup = Stopwatch.StartNew();
        while (warmup.ElapsedMilliseconds < 2000) workload.Read();
        var samples = new double[7];
        long allocated = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = Sample(workload);
            samples[i] = sample.Microseconds;
            allocated += sample.AllocatedBytes;
        }
        double mean = samples.Average();
        double deviation = Math.Sqrt(samples.Sum(x => Math.Pow(x - mean, 2)) / (samples.Length - 1));
        Console.WriteLine(FormattableString.Invariant($"{workload.Format},{workload.Scenario},{mean:F2},{deviation:F2},{allocated / samples.Length},{workload.Expected}"));
    }

    private static (double Microseconds, long AllocatedBytes) Sample(Workload workload)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        int count = 0;
        do { workload.Read(); count++; } while (timer.ElapsedMilliseconds < 200);
        return (timer.Elapsed.TotalMicroseconds / count, (GC.GetAllocatedBytesForCurrentThread() - before) / count);
    }

    private sealed class Workload
    {
        private readonly byte[] _data;
        private readonly KeyValueReader _reader;
        public string Format { get; }
        public string Scenario { get; }
        public long Expected { get; }

        public Workload(bool logfmt, string scenario)
        {
            Format = logfmt ? "Logfmt" : "Ltsv";
            Scenario = scenario;
            string value = scenario == "CrossBuffer" ? new string('x', 8192) : "東京 café";
            string suffix = scenario == "Escaped" ? "\\n\\\"ok\\\"" : "";
            string line = logfmt
                ? $"id=42 message=\"{value}{suffix}\" active\r\n"
                : $"id:42\tmessage:{value}{suffix}\tactive:\r\n";
            _data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, RecordCount)));
            var options = new ParserOptimizationOptions(BufferSize: 4096);
            _reader = logfmt
                ? new LogfmtReaderBuilder().WithParserOptimizations(options).Build()
                : new LtsvReaderBuilder().WithParserOptimizations(options).Build();
            int decodedSuffixLength = logfmt ? 5 : 8;
            Expected = RecordCount * (42L + value.Length + (scenario == "Escaped" ? decodedSuffixLength : 0));
        }

        public long Read()
        {
            using var reader = _reader.ToDataReader(new MemoryStream(_data, writable: false));
            long checksum = 0;
            int count = 0;
            while (reader.Read())
            {
                checksum += reader.GetString(1).Length + reader.GetInt32(0);
                count++;
            }
            if (count != RecordCount) throw new InvalidOperationException("Missing benchmark records.");
            return checksum;
        }
    }
}
