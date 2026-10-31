using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PocketCsvReader.Configuration;
using PocketCsvReader.WebLogs;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CommonLogComparison
{
    [Params(10_000, 100_000)]
    public int RecordCount { get; set; }

    private byte[] _csv = [];
    private byte[] _clf = [];
    private CsvReader _csvReader = null!;
    private CommonLogReader _commonReader = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new ParserOptimizationOptions(BufferSize: 4096, RowCountAtStart: false);
        var resource = new ResourceDescriptor(Encoding: "utf-8");
        var dialect = new DialectDescriptorBuilder()
            .WithDelimiter(',').WithQuoteChar('"').WithDoubleQuote()
            .WithLineTerminator("\n").WithoutHeader().WithNullSequence("-").Build();
        _csvReader = new CsvReader(new CsvProfile(dialect, resource: resource)
        {
            ParserOptimizations = options,
            StreamInitialization = StreamInitializationOptions.ForwardOnly
        });
        _commonReader = new CommonLogReader(new WebLogProfile(
            WebLogFormat.Common, resource: resource, parserOptimizations: options));

        // Encode equivalent seven-field entries once, outside the measured operations.
        var csv = new StringBuilder();
        var clf = new StringBuilder();
        for (var i = 0; i < RecordCount; i++)
        {
            var host = $"192.0.2.{i % 254 + 1}";
            var user = i % 3 == 0 ? "-" : $"user{i % 100}";
            var timestamp = i % 2 == 0 ? "10/Oct/2000:13:55:36 -0700" : "11/Oct/2000:09:15:00 +0530";
            var request = $"GET /items/{i % 1000}?page={i % 10} HTTP/1.1";
            var status = i % 10 == 0 ? 404 : 200;
            var bytes = i % 7 == 0 ? "-" : (1024L + i).ToString(CultureInfo.InvariantCulture);
            csv.Append(CultureInfo.InvariantCulture, $"{host},-,{user},{timestamp},\"{request}\",{status},{bytes}\n");
            clf.Append(CultureInfo.InvariantCulture, $"{host} - {user} [{timestamp}] \"{request}\" {status} {bytes}\n");
        }
        _csv = Encoding.UTF8.GetBytes(csv.ToString());
        _clf = Encoding.UTF8.GetBytes(clf.ToString());

        // Reject a comparison that reads different rows, loses fields, or skips conversion.
        using var csvRows = ReadCsvRecords().GetEnumerator();
        using var clfRows = _commonReader.ToCommonLog(new MemoryStream(_clf, writable: false)).GetEnumerator();
        var count = 0;
        while (csvRows.MoveNext())
        {
            if (!clfRows.MoveNext() || csvRows.Current != clfRows.Current ||
                csvRows.Current.Timestamp?.Offset != clfRows.Current.Timestamp?.Offset)
                throw new InvalidOperationException($"CSV and CLF differ at row {count + 1}.");
            count++;
        }
        if (clfRows.MoveNext() || count != RecordCount || CsvRows() != RecordCount || CommonLogRows() != RecordCount)
            throw new InvalidOperationException("CSV and CLF row counts differ.");
        if (CsvTyped() != CommonLogTyped())
            throw new InvalidOperationException("Typed checksums differ.");
        Console.WriteLine($"Equivalent input: {RecordCount:N0} rows; CSV {_csv.Length:N0} bytes; CLF {_clf.Length:N0} bytes.");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Rows")]
    public int CsvRows()
    {
        using var reader = _csvReader.ToDataReader(new MemoryStream(_csv, writable: false));
        var count = 0;
        while (reader.Read())
            count++;
        return count;
    }

    [Benchmark, BenchmarkCategory("Rows")]
    public int CommonLogRows()
    {
        using var reader = _commonReader.ToDataReader(new MemoryStream(_clf, writable: false));
        var count = 0;
        while (reader.Read())
            count++;
        return count;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Typed")]
    public long CsvTyped() => Consume(ReadCsvRecords());

    [Benchmark, BenchmarkCategory("Typed")]
    public long CommonLogTyped()
        => Consume(_commonReader.ToCommonLog(new MemoryStream(_clf, writable: false)));

    private IEnumerable<CommonLog> ReadCsvRecords()
    {
        using var reader = _csvReader.ToDataReader(new MemoryStream(_csv, writable: false));
        while (reader.Read())
            yield return new CommonLog(
                reader.GetFieldValue<string?>(0),
                reader.GetFieldValue<string?>(1),
                reader.GetFieldValue<string?>(2),
                reader.GetFieldValue<DateTimeOffset?>(3, (ReadOnlySpan<char> value) =>
                    DateTimeOffset.ParseExact(value, "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture)),
                reader.GetFieldValue<string?>(4),
                reader.GetFieldValue<int?>(5, (ReadOnlySpan<char> value) =>
                    int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                reader.GetFieldValue<long?>(6, (ReadOnlySpan<char> value) =>
                    long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)));
    }

    private static long Consume(IEnumerable<CommonLog> entries)
    {
        long checksum = 0;
        foreach (var entry in entries)
            checksum += (entry.RemoteHost?.Length ?? 0) + (entry.Identity?.Length ?? 0) +
                (entry.AuthenticatedUser?.Length ?? 0) + (entry.Request?.Length ?? 0) +
                (entry.Timestamp?.Ticks ?? 0) + (entry.StatusCode ?? 0) + (entry.ResponseBytes ?? 0);
        return checksum;
    }
}
