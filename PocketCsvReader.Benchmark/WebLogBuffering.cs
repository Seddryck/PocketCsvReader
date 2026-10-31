using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PocketCsvReader.WebLogs;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class WebLogBuffering
{
    private const int RecordCount = 512;

    [Params(WebLogFormat.Common, WebLogFormat.W3cExtended)]
    public WebLogFormat Format { get; set; }

    [Params(32, 8192)]
    public int PathLength { get; set; }

    private byte[] _data = [];
    private WebLogReader _reader = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reader = Format == WebLogFormat.Common ? new CommonLogReader() : new W3cExtendedLogReader();
        var path = "/" + new string('x', PathLength);
        var line = Format == WebLogFormat.Common
            ? $"127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"{path}\" 200 64\n"
            : $"127.0.0.1 - frank 2000-10-10T13:55:36-07:00 {path} 200 64\n";
        var header = Format == WebLogFormat.Common ? "" : "#Fields: host identity user timestamp request status bytes\n";
        _data = Encoding.UTF8.GetBytes(header + string.Concat(Enumerable.Repeat(line, RecordCount)));
        if (Rows() != RecordCount || Typed() != RecordCount * (path.Length + 200L + 64L))
            throw new InvalidOperationException("The benchmark did not consume the expected records.");
    }

    [Benchmark]
    public int Rows()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_data, writable: false));
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public long Typed()
    {
        var stream = new MemoryStream(_data, writable: false);
        var entries = _reader is CommonLogReader common
            ? common.ToCommonLog(stream)
            : _reader.To<CommonLog>(stream, static row => new CommonLog(
                row.GetFieldValue<string?>(0), row.GetFieldValue<string?>(1), row.GetFieldValue<string?>(2),
                row.GetFieldValue<DateTimeOffset?>(3, (ReadOnlySpan<char> value) =>
                    DateTimeOffset.ParseExact(value, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)),
                row.GetFieldValue<string?>(4),
                row.GetFieldValue<int?>(5, (ReadOnlySpan<char> value) => int.Parse(value, CultureInfo.InvariantCulture)),
                row.GetFieldValue<long?>(6, (ReadOnlySpan<char> value) => long.Parse(value, CultureInfo.InvariantCulture))));
        long checksum = 0;
        foreach (var entry in entries)
        {
            if (entry.Timestamp?.Offset != TimeSpan.FromHours(-7) || entry.AuthenticatedUser != "frank" || entry.Identity is not null)
                throw new InvalidOperationException("Typed field conversion failed.");
            checksum += entry.Request!.Length + entry.StatusCode!.Value + entry.ResponseBytes!.Value;
        }
        return checksum;
    }
}
