using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.KeyValue.Configuration;
using PocketCsvReader.Ndjson.Configuration;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class WebLogParserComparison
{
    public const int RecordCount = 50_000;
    public enum LogParser { CsvChars, CsvUtf8, FixedWidth, Ndjson, Json, Logfmt, Ltsv, CommonLog, CombinedLog, W3c }

    [ParamsAllValues]
    public LogParser Parser { get; set; }

    private byte[] _data = [];
    private Func<Stream, IDataReader> _open = null!;
    private int _requestOrdinal = 4;
    private int _statusOrdinal = 5;
    private int _bytesOrdinal = 6;

    [GlobalSetup]
    public void Setup()
    {
        var options = new ParserOptimizationOptions(BufferSize: 4096, ReadAhead: false, RowCountAtStart: false);
        _open = Parser switch
        {
            LogParser.CsvChars or LogParser.CsvUtf8 => new CsvReaderBuilder()
                .WithDialect(d => d.WithoutHeader().WithLineTerminator("\n"))
                .WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options)
                .WithStreamInitialization(Parser == LogParser.CsvUtf8
                    ? StreamInitializationOptions.Utf8 : StreamInitializationOptions.ForwardOnly)
                .Build().ToDataReader,
            LogParser.FixedWidth => new FixedWidthReaderBuilder()
                .WithField("host", 0, 15).WithField("identity", 15, 2).WithField("user", 17, 10)
                .WithField("timestamp", 27, 26).WithField("request", 53, 64)
                .WithField("status", 117, 4).WithField("bytes", 121, 10)
                .WithLineTerminator("\n").WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.Ndjson => new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
                .WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.Json => new JsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.Logfmt => new LogfmtReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.Ltsv => new LtsvReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.CommonLog => new CommonLogReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.CombinedLog => new CombinedLogReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            LogParser.W3c => new W3cExtendedLogReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
                .WithParserOptimizations(options).Build().ToDataReader,
            _ => throw new ArgumentOutOfRangeException()
        };

        var data = new StringBuilder();
        if (Parser == LogParser.Json) data.Append('[');
        if (Parser == LogParser.W3c)
        {
            data.Append("#Fields: c-ip cs-username date time cs-method cs-uri-stem sc-status sc-bytes\n");
            _requestOrdinal = 5; _statusOrdinal = 6; _bytesOrdinal = 7;
        }
        for (int i = 0; i < RecordCount; i++)
        {
            var row = Expected(i);
            string host = $"192.0.2.{i % 254 + 1}";
            string user = $"user{i % 100}";
            string[] fields = [host, "-", user, "01/Nov/2026:12:34:56 +0000", row.Request,
                row.Status.ToString(CultureInfo.InvariantCulture), row.Bytes.ToString(CultureInfo.InvariantCulture)];
            string[] names = ["host", "identity", "user", "timestamp", "request", "status", "bytes"];
            switch (Parser)
            {
                case LogParser.CsvChars:
                case LogParser.CsvUtf8:
                    data.AppendJoin(',', fields).Append('\n'); break;
                case LogParser.FixedWidth:
                    int[] widths = [15, 2, 10, 26, 64, 4, 10];
                    for (int j = 0; j < fields.Length; j++) data.Append(fields[j].PadRight(widths[j]));
                    data.Append('\n'); break;
                case LogParser.Ndjson:
                case LogParser.Json:
                    if (Parser == LogParser.Json && i > 0) data.Append(',');
                    data.Append(System.Text.Json.JsonSerializer.Serialize(new
                    { host, identity = "-", user, timestamp = fields[3], request = row.Request, status = row.Status, bytes = row.Bytes }));
                    data.Append('\n'); break;
                case LogParser.Logfmt:
                case LogParser.Ltsv:
                    for (int j = 0; j < fields.Length; j++)
                    {
                        if (j > 0) data.Append(Parser == LogParser.Ltsv ? '\t' : ' ');
                        data.Append(names[j]).Append(Parser == LogParser.Ltsv ? ':' : '=');
                        if (Parser == LogParser.Logfmt) data.Append('"');
                        data.Append(fields[j]);
                        if (Parser == LogParser.Logfmt) data.Append('"');
                    }
                    data.Append('\n'); break;
                case LogParser.CommonLog:
                case LogParser.CombinedLog:
                    data.Append(CultureInfo.InvariantCulture, $"{host} - {user} [{fields[3]}] \"{row.Request}\" {row.Status} {row.Bytes}");
                    if (Parser == LogParser.CombinedLog) data.Append(" \"https://example.test/\" \"Benchmark/1.0\"");
                    data.Append('\n'); break;
                case LogParser.W3c:
                    // W3C stores method separately; the URI field is projected to the common request below.
                    data.Append(CultureInfo.InvariantCulture, $"{host} {user} 2026-11-01 12:34:56 GET /items/{i % 1000}?page={i % 10} {row.Status} {row.Bytes}\n");
                    break;
            }
        }
        if (Parser == LogParser.Json) data.Append(']');
        _data = Encoding.UTF8.GetBytes(data.ToString());

        // Check every projected value and row, rather than trusting matching aggregate checksums.
        using var reader = _open(new MemoryStream(_data, writable: false));
        int count = 0;
        while (reader.Read())
        {
            if (count >= RecordCount || Project(reader) != Expected(count))
                throw new InvalidOperationException($"{Parser}: unexpected output at row {count + 1}.");
            count++;
        }
        if (count != RecordCount) throw new InvalidOperationException($"{Parser}: read {count} rows.");
        Console.WriteLine($"Validated {Parser}: {count} rows, {_data.Length} input bytes, three values per row.");
    }

    private static (string Request, int Status, long Bytes) Expected(int i)
        => ($"GET /items/{i % 1000}?page={i % 10} HTTP/1.1", i % 10 == 0 ? 404 : 200, 1024L + i);

    private (string Request, int Status, long Bytes) Project(IDataReader reader)
        => (Parser == LogParser.W3c ? $"GET {reader.GetString(_requestOrdinal)} HTTP/1.1" : reader.GetString(_requestOrdinal),
            reader.GetInt32(_statusOrdinal), reader.GetInt64(_bytesOrdinal));

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

    public static void RunMatrix()
    {
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine("Parser,Records,InputBytes,WallMeanMs,WallStdDevMs,CpuMeanMs,AllocatedBytes,BytesPerRow");
        using var process = Process.GetCurrentProcess();
        foreach (var parser in Enum.GetValues<LogParser>())
        {
            var benchmark = new WebLogParserComparison { Parser = parser };
            benchmark.Setup();
            var warmup = Stopwatch.StartNew();
            while (warmup.Elapsed.TotalSeconds < 2) benchmark.ReadThreeValues();
            var wall = new double[7];
            var cpu = new double[7];
            var allocated = new double[7];
            long expectedChecksum = benchmark.ReadThreeValues();
            for (int sample = 0; sample < wall.Length; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                // Long batches reduce quantization error in Windows process CPU accounting.
                var timer = new Stopwatch();
                var cpuBefore = process.TotalProcessorTime;
                long before = GC.GetAllocatedBytesForCurrentThread();
                timer.Start();
                int count = 0;
                do
                {
                    if (benchmark.ReadThreeValues() != expectedChecksum) throw new InvalidOperationException("Checksum changed.");
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
            Console.WriteLine(FormattableString.Invariant($"{parser},{RecordCount},{benchmark._data.Length},{mean:F3},{deviation:F3},{cpu.Average():F3},{allocated.Average():F0},{allocated.Average() / RecordCount:F2}"));
        }
    }
}
