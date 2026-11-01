using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PocketCsvReader;
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

// Same source is compiled against the baseline and candidate package roots.
// A worker measures one workload; the script alternates worker order in pairs.
if (args.Length != 5) throw new ArgumentException("label format projected raw size");
string label = args[0], format = args[1], size = args[4];
bool projected = bool.Parse(args[2]), raw = bool.Parse(args[3]);
const int rows = 50000;
int padding = size switch { "small" => 0, "cross" => 300, "large" => 8000, _ => throw new ArgumentException("size") };
int buffer = size == "cross" ? 128 : 65536;
var input = new StringBuilder();
if (format == "json") input.Append('[');
for (int i = 0; i < rows; i++)
{
    if (format == "json" && i > 0) input.Append(',');
    input.Append(JsonSerializer.Serialize(new { host = "192.0.2.1", identity = "-", user = "user1",
        timestamp = "01/Nov/2026:12:34:56 +0000", request = Request(i), status = Status(i), bytes = Bytes(i), unused = new string('x', padding) }));
    input.Append('\n');
}
if (format == "json") input.Append(']');
byte[] data = Encoding.UTF8.GetBytes(input.ToString());
input.Clear();
Func<Stream, IDataReader> open;
var options = new ParserOptimizationOptions(BufferSize: buffer, ReadAhead: false, RowCountAtStart: false);
if (format == "json")
{
    var builder = new JsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8")).WithParserOptimizations(options);
    if (projected) builder.WithProjection(p => p.Property("request").Property("status").Property("bytes"));
    open = builder.Build().ToDataReader;
}
else
{
    var builder = new NdjsonReaderBuilder().WithResource(r => r.WithEncoding("utf-8"))
        .WithDialect(d => d.WithLineTerminator("\n")).WithParserOptimizations(options);
    if (projected) builder.WithProjection(p => p.Property("request").Property("status").Property("bytes"));
    open = builder.Build().ToDataReader;
}
int ordinal = projected ? 0 : 4;
// Validate every projected value, separately from warmup and timed operations.
using (var reader = open(new MemoryStream(data, false)))
{
    int i = 0;
    while (reader.Read())
    {
        if (reader.GetString(ordinal) != Request(i) || reader.GetInt32(ordinal + 1) != Status(i) || reader.GetInt64(ordinal + 2) != Bytes(i))
            throw new InvalidOperationException($"Bad row {i}");
        i++;
    }
    if (i != rows) throw new InvalidOperationException("Row count");
}
long Read()
{
    using var reader = open(new MemoryStream(data, false));
    long checksum = 0;
    while (reader.Read())
    {
        checksum++;
        if (!raw) checksum += reader.GetString(ordinal).Length + reader.GetInt32(ordinal + 1) + reader.GetInt64(ordinal + 2);
    }
    return checksum;
}
long expected = Read();
var warmup = Stopwatch.StartNew();
while (warmup.Elapsed.TotalSeconds < 2) if (Read() != expected) throw new InvalidOperationException("Warmup");
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
using var process = Process.GetCurrentProcess();
var timer = new Stopwatch();
var cpu = process.TotalProcessorTime;
long allocated = GC.GetAllocatedBytesForCurrentThread();
int operations = 0;
timer.Start();
do
{
    if (Read() != expected) throw new InvalidOperationException("Checksum");
    operations++;
} while (timer.Elapsed.TotalSeconds < 2);
timer.Stop();
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds / operations;
Console.WriteLine(string.Join(',', label, format, projected, raw, size, rows, buffer, operations,
    (timer.Elapsed.TotalMilliseconds / operations).ToString("F3", CultureInfo.InvariantCulture),
    cpuMs.ToString("F3", CultureInfo.InvariantCulture), ((double)allocated / operations).ToString("F0", CultureInfo.InvariantCulture),
    Convert.ToHexString(SHA256.HashData(data))));
static string Request(int i) => $"GET /items/{i % 1000}?page={i % 10} HTTP/1.1";
static int Status(int i) => i % 10 == 0 ? 404 : 200;
static long Bytes(int i) => 1024L + i;
