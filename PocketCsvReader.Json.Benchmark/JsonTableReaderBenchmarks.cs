using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Benchmark;

[MemoryDiagnoser]
public class JsonTableReaderBenchmarks
{
    private byte[] _input = null!;
    private JsonTableReader _reader = null!;

    // Rows fit comfortably, straddle a buffer boundary, or span several buffers.
    [Params(24, 260, 4096)]
    public int TextLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int bufferSize = 256;
        var row = "[1,\"" + new string('x', TextLength) + "\"]";
        var document = "[[\"id\",\"name\"]," + string.Join(',', Enumerable.Repeat(row, 256)) + "]";
        _input = Encoding.UTF8.GetBytes(document);
        _reader = new JsonTableReader(new JsonProfile(
            parserOptimizations: new ParserOptimizationOptions(BufferSize: bufferSize, ReadAhead: false, RowCountAtStart: false)));
    }

    [Benchmark(Baseline = true)]
    public int ReadRows()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_input));
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    [Benchmark]
    public int ReadNumbers()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_input));
        var sum = 0;
        while (reader.Read()) sum += reader.GetInt32(0);
        return sum;
    }

    [Benchmark]
    public int ReadStrings()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_input));
        var length = 0;
        while (reader.Read()) length += reader.GetString(1).Length;
        return length;
    }
}
