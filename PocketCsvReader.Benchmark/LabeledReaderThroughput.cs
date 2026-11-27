using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bogus;
using PocketCsvReader.Json;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.KeyValue;
using PocketCsvReader.KeyValue.Configuration;
using PocketCsvReader.Ndjson;
using PocketCsvReader.Ndjson.Configuration;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.Benchmark;

internal static class LabeledReaderThroughput
{
    private const int RowCount = 50_000;
    private const int DistinctNames = 3_500;
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] Universities =
    [
        "KU Leuven", "Ghent University", "Université libre de Bruxelles", "Université de Liège",
        "University of California, \"Berkeley\"", "Universiteit Antwerpen",
    ];
    private static readonly ParserOptimizationOptions Options = new(ReadAhead: false, BufferSize: 65 * 1024);

    public static void Run(string mode)
    {
        if (mode is not ("standard" or "pool" or "lines"))
            throw new ArgumentException("Choose standard, pool, or lines.", nameof(mode));

        var options = Options with { PoolStringLastValueHint = mode == "pool" };

        Console.WriteLine("Format,Distribution,Operation,Mode,MedianCpuMs,MedianElapsedMs,AllocatedBytes,Checksum");
        if (mode == "lines")
            RunLineReaders(mode);
        else
            RunEnrollments(mode, options);
    }

    public static void RunScan(string format)
    {
        if (format is not ("JSON" or "NDJSON" or "Logfmt" or "LTSV"))
            throw new ArgumentException("Choose JSON, NDJSON, Logfmt, or LTSV.", nameof(format));

        Console.WriteLine("Format,Distribution,Operation,Mode,MedianCpuMs,MedianElapsedMs,AllocatedBytes,Checksum");
        if (format is "JSON" or "NDJSON")
        {
            var records = Generate("RealCsv");
            if (format == "JSON")
            {
                var profile = new JsonProfile(parserOptimizations: Options);
                var data = CreateJson(records, array: true);
                Measure(format, "RealCsv", "Scan", "scan", () => Read(data, stream => new JsonDataReader(stream, profile), readDate: null), RowCount,
                    warmupMilliseconds: 800, sampleMilliseconds: 400, sampleCount: 3);
            }
            else
            {
                var profile = new NdjsonProfile(new(LineTerminator: "\n"), parserOptimizations: Options);
                var data = CreateJson(records, array: false);
                Measure(format, "RealCsv", "Scan", "scan", () => Read(data, stream => new NdjsonDataReader(stream, profile), readDate: null), RowCount,
                    warmupMilliseconds: 800, sampleMilliseconds: 400, sampleCount: 3);
            }
            return;
        }

        var lines = new StringBuilder();
        for (var i = 0; i < RowCount; i++)
        {
            lines.Append(format == "LTSV"
                ? FormattableString.Invariant($"id:{i}\troute:/courses\tstatus:200\tbytes:512\tuser:frank\n")
                : FormattableString.Invariant($"id={i} route=/courses status=200 bytes=512 user=frank\n"));
        }

        var bytes = Encoding.UTF8.GetBytes(lines.ToString());
        var reader = format == "LTSV"
            ? new LtsvReaderBuilder().WithParserOptimizations(Options).Build()
            : new LogfmtReaderBuilder().WithParserOptimizations(Options).Build();
        using (var check = reader.ToDataReader(new MemoryStream(bytes, writable: false)))
        {
            var verified = 0;
            while (check.Read())
            {
                if (check.GetInt32(0) != verified || check.GetInt32(2) != 200 || check.GetString(4) != "frank")
                    throw new InvalidOperationException("Unexpected key/value record.");
                verified++;
            }
            if (verified != RowCount)
                throw new InvalidOperationException("Unexpected key/value row count.");
        }
        Measure(format, "Requests", "Scan", "scan", () => Read(bytes, stream => reader.ToDataReader(stream), readDate: null), RowCount,
            warmupMilliseconds: 800, sampleMilliseconds: 400, sampleCount: 3);
    }

    private static void RunEnrollments(string mode, ParserOptimizationOptions options)
    {
        foreach (var distribution in new[] { "RealCsv", "Clustered", "MostlyUnique" })
        {
            var records = Generate(distribution);
            var json = CreateJson(records, array: true);
            var ndjson = CreateJson(records, array: false);
            var expected = records.Aggregate(0L, AddEnrollment);
            var jsonProfile = new JsonProfile(parserOptimizations: options);
            var ndjsonProfile = new NdjsonProfile(new(LineTerminator: "\n"), parserOptimizations: options);
            RunEnrollment("JSON", distribution, json, stream => new JsonDataReader(stream, jsonProfile),
                reader => ((JsonDataReader)reader).GetDate(6), expected, mode);
            RunEnrollment("NDJSON", distribution, ndjson, stream => new NdjsonDataReader(stream, ndjsonProfile),
                reader => ((NdjsonDataReader)reader).GetDate(6), expected, mode);
        }
    }

    private static void RunLineReaders(string mode)
    {
        var logs = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, RowCount).Select(i =>
            FormattableString.Invariant($"127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET /courses/{i} HTTP/1.1\" 200 512\r\n"))));
        var keyValues = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, RowCount).Select(i =>
            FormattableString.Invariant($"id={i} route=/courses status=200 bytes=512 user=frank\n"))));
        var webLogs = new CommonLogReaderBuilder().WithParserOptimizations(Options).Build();
        var logfmt = new LogfmtReaderBuilder().WithParserOptimizations(Options).Build();
        Measure("CommonLog", "Requests", "Scan", mode, () => Read(logs, stream => webLogs.ToDataReader(stream), readDate: null), RowCount);
        Measure("Logfmt", "Requests", "Scan", mode, () => Read(keyValues, stream => logfmt.ToDataReader(stream), readDate: null), RowCount);
    }

    private static void RunEnrollment(string format, string distribution, byte[] data, Func<Stream, IDataReader> open,
        Func<IDataReader, DateOnly> readDate, long expected, string mode)
    {
        if (mode == "standard")
            Measure(format, distribution, "Scan", mode, () => Read(data, open, readDate: null), RowCount);
        Measure(format, distribution, "Typed", mode, () => Read(data, open, readDate), expected);
    }

    private static long Read(byte[] data, Func<Stream, IDataReader> open, Func<IDataReader, DateOnly>? readDate)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = open(stream);
        long checksum = 0;
        var rows = 0;
        while (reader.Read())
        {
            if (readDate is not null)
            {
                var date = readDate(reader);
                checksum = AddEnrollment(checksum, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetDecimal(4), reader.GetInt16(5), date);
            }
            rows++;
        }
        if (rows != RowCount)
            throw new InvalidOperationException($"Expected {RowCount} rows, read {rows}.");
        return readDate is null ? rows : checksum;
    }

    private static void Measure(string format, string distribution, string operation, string mode, Func<long> run, long expected,
        int warmupMilliseconds = 600, int sampleMilliseconds = 500, int sampleCount = 5)
    {
        if (run() != expected)
            throw new InvalidOperationException($"Unexpected checksum for {format}/{distribution}/{operation}.");
        // Warm tiered compilation and the shared buffer pools before capturing repeated process CPU samples.
        var warmup = Stopwatch.StartNew();
        do { run(); } while (warmup.ElapsedMilliseconds < warmupMilliseconds);

        using var process = Process.GetCurrentProcess();
        var cpu = new double[sampleCount];
        var elapsed = new double[sampleCount];
        var allocations = new long[sampleCount];
        for (var sample = 0; sample < sampleCount; sample++)
        {
            process.Refresh();
            var cpuStart = process.TotalProcessorTime;
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            int passes = 0;
            do { run(); passes++; } while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < sampleMilliseconds);
            allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / passes;
            elapsed[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / passes;
            process.Refresh();
            cpu[sample] = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / passes;
        }
        Array.Sort(cpu);
        Array.Sort(elapsed);
        Array.Sort(allocations);
        Console.WriteLine(FormattableString.Invariant(
            $"{format},{distribution},{operation},{mode},{cpu[sampleCount / 2]:F3},{elapsed[sampleCount / 2]:F3},{allocations[sampleCount / 2]},{expected}"));
    }

    private static Enrollment[] Generate(string distribution)
    {
        var faker = new Faker { Random = new Randomizer(42) };
        var names = new List<(string First, string Last)>(DistinctNames);
        var seen = new HashSet<(string, string)>();
        while (names.Count < DistinctNames)
        {
            var name = (faker.Name.FirstName(), faker.Name.LastName());
            if (seen.Add(name))
                names.Add(name);
        }

        var random = new Random(42);
        var assignments = Enumerable.Range(0, RowCount).Select(i => i < DistinctNames ? i : random.Next(DistinctNames)).ToArray();
        random.Shuffle(assignments.AsSpan());
        var records = new Enrollment[RowCount];
        var firstRegistration = new DateOnly(2022, 1, 1);
        for (var i = 0; i < records.Length; i++)
        {
            var group = distribution == "Clustered" ? i / 64 : random.Next(RowCount);
            var (first, last) = names[distribution == "Clustered" ? group % names.Count : assignments[i]];
            var university = Universities[group % Universities.Length];
            var quarter = FormattableString.Invariant($"{2024 + (group % 3)}-Q{1 + (group % 4)}");
            var date = distribution == "Clustered" ? firstRegistration.AddDays(group) : firstRegistration.AddDays(random.Next(730));
            if (distribution == "MostlyUnique")
            {
                university = FormattableString.Invariant($"{university} / campus {i}");
                first = FormattableString.Invariant($"{first} {i}");
                last = FormattableString.Invariant($"{last} {i}");
                quarter = FormattableString.Invariant($"{quarter}/{i}");
                date = firstRegistration.AddDays(i);
            }
            records[i] = new(university, first, last, quarter, random.Next(201) / 10m, (short)random.Next(1, 181), date);
        }
        return records;
    }

    private static byte[] CreateJson(Enrollment[] records, bool array)
    {
        if (array)
            return JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);
        var lines = new StringBuilder();
        foreach (var record in records)
            lines.Append(JsonSerializer.Serialize(record, JsonOptions)).Append('\n');
        return Encoding.UTF8.GetBytes(lines.ToString());
    }

    private static long AddEnrollment(long checksum, Enrollment record)
        => AddEnrollment(checksum, record.University, record.FirstName, record.LastName, record.Quarter,
            record.Score, record.Credits, record.RegistrationDate);

    private static long AddEnrollment(long checksum, string university, string firstName, string lastName,
        string quarter, decimal score, short credits, DateOnly date)
    {
        foreach (var character in university) checksum = unchecked((checksum * 31) + character);
        foreach (var character in firstName) checksum = unchecked((checksum * 31) + character);
        foreach (var character in lastName) checksum = unchecked((checksum * 31) + character);
        foreach (var character in quarter) checksum = unchecked((checksum * 31) + character);
        return unchecked((checksum * 31) + (long)(score * 10) + (31L * credits) + (961L * date.DayNumber));
    }

    private sealed record Enrollment(string University, string FirstName, string LastName, string Quarter,
        decimal Score, short Credits, DateOnly RegistrationDate);
}
