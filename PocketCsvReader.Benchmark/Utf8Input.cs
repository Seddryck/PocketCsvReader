using System.Text;
using BenchmarkDotNet.Attributes;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob]
public class Utf8Input
{
    [Params("AsciiNumeric", "UnicodeStrings", "AsciiWideSkipped", "UnicodeWideSkipped", "MultilineStrings", "AsciiWideStrings")]
    public string Scenario { get; set; } = "AsciiNumeric";

    private byte[] _data = [];
    private CsvProfile _chars = null!, _bytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        int columns = Scenario.Contains("Wide") ? 24 : 3;
        string value = Scenario.Contains("Unicode") ? "東京é😀"
            : Scenario.Contains("Multiline") ? "\"hello\r\nworld\"" : "12345";
        string row = string.Join(',', Enumerable.Repeat(value, columns)) + "\r\n";
        _data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(row, 2000)));
        _chars = new(new(Header: false)) { StreamInitialization = new(InputMode: CsvInputMode.DecodedChars) };
        _bytes = new(new(Header: false)) { StreamInitialization = StreamInitializationOptions.Utf8 };
    }

    [Benchmark(Baseline = true)]
    public long DecodedChars() => Read(_chars);

    [Benchmark]
    public long Utf8Bytes() => Read(_bytes);

    private long Read(CsvProfile profile)
    {
        using var stream = new MemoryStream(_data, writable: false);
        using var reader = new CsvDataReader(stream, profile);
        long sum = 0;
        while (reader.Read())
        {
            if (Scenario.EndsWith("Skipped"))
                sum += reader.FieldCount;
            else if (Scenario.EndsWith("Numeric"))
                for (int i = 0; i < reader.FieldCount; i++) sum += reader.GetInt32(i);
            else
                for (int i = 0; i < reader.FieldCount; i++) sum += reader.GetString(i).Length;
        }
        return sum;
    }
}
