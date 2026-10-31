using System.Diagnostics;
using System.Text;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.Benchmark;

// Run the same Release harness on the baseline and implementation. Each sample
// consumes 512 records, including stream/reader setup and schema resolution.
public static class KeyValueBuffering
{
    public static void RunMatrix()
    {
        Console.WriteLine("Format,Scenario,MeanUs,StdDevUs,AllocatedBytes,Checksum");
        foreach (bool logfmt in new[] { false, true })
        foreach (string scenario in new[] { "Simple", "Escaped", "CrossBuffer" })
        {
            string value = scenario == "CrossBuffer" ? new string('x', 8192) : "東京 café";
            string line = logfmt
                ? $"id=42 message=\"{value}{(scenario == "Escaped" ? "\\n\\\"ok\\\"" : "")}\" active\r\n"
                : $"id:42\tmessage:{value}{(scenario == "Escaped" ? "\\n\\\"ok\\\"" : "")}\tactive:\r\n";
            var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 512)));
            var options = new ParserOptimizationOptions(BufferSize: 4096);
            var formatReader = logfmt
                ? new LogfmtReaderBuilder().WithParserOptimizations(options).Build()
                : new LtsvReaderBuilder().WithParserOptimizations(options).Build();
            long Read()
            {
                using var reader = formatReader.ToDataReader(new MemoryStream(data, writable: false));
                long checksum = 0;
                int count = 0;
                while (reader.Read())
                {
                    checksum += reader.GetString(1).Length + reader.GetInt32(0);
                    count++;
                }
                if (count != 512) throw new InvalidOperationException("Missing benchmark records.");
                return checksum;
            }
            long expected = 512L * (42 + value.Length + (scenario == "Escaped" ? (logfmt ? 5 : 8) : 0));
            if (Read() != expected) throw new InvalidOperationException("Incorrect benchmark values.");
            var warmup = Stopwatch.StartNew();
            while (warmup.ElapsedMilliseconds < 2000) Read();
            var samples = new double[7];
            long allocated = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                int count = 0;
                do { Read(); count++; } while (timer.ElapsedMilliseconds < 200);
                samples[i] = timer.Elapsed.TotalMicroseconds / count;
                allocated += (GC.GetAllocatedBytesForCurrentThread() - before) / count;
            }
            double mean = samples.Average();
            double deviation = Math.Sqrt(samples.Sum(x => Math.Pow(x - mean, 2)) / (samples.Length - 1));
            Console.WriteLine(FormattableString.Invariant($"{(logfmt ? "Logfmt" : "Ltsv")},{scenario},{mean:F2},{deviation:F2},{allocated / samples.Length},{expected}"));
        }
    }
}
