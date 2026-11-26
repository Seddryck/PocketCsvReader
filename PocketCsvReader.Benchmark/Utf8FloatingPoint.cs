using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
public class Utf8FloatingPoint
{
    private const int RowCount = 2000;
    private byte[] _data = [];
    private CsvReader _chars = null!;
    private CsvReader _bytes = null!;
    private CsvReader _fallback = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string row = "12.375,123456.78901,-4.75e-6,3.141592653589793,\"1,234.5\",-1.23e-123\r\n";
        _data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(row, RowCount)));
        _chars = CreateBuilder(CsvInputMode.DecodedChars).Build();
        _bytes = CreateBuilder(CsvInputMode.Utf8Bytes).Build();
        _fallback = CreateBuilder(CsvInputMode.Utf8Bytes)
            .WithParsers(p => p
                .WithParser<float>(static (ReadOnlySpan<char> text) => float.Parse(text, CultureInfo.InvariantCulture))
                .WithParser<double>(static (ReadOnlySpan<char> text) => double.Parse(text, CultureInfo.InvariantCulture)))
            .Build();
    }

    [Benchmark(Baseline = true)]
    public double DecodedChars() => Read(_chars);

    [Benchmark]
    public double Utf8Bytes() => Read(_bytes);

    [Benchmark]
    public double Utf8WithCharacterParsers() => Read(_fallback);

    private static CsvReaderBuilder CreateBuilder(CsvInputMode mode)
        => new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(new StreamInitializationOptions(InputMode: mode));

    private double Read(CsvReader csv)
    {
        using var stream = new MemoryStream(_data, writable: false);
        using var reader = csv.ToDataReader(stream);
        double sum = 0;
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i += 2)
            {
                sum += reader.GetFloat(i);
                sum += reader.GetDouble(i + 1);
            }
        }

        return sum;
    }
}
