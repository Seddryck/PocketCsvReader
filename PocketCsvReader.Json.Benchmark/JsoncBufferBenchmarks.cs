using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Benchmark;

[MemoryDiagnoser]
public class JsoncBufferBenchmarks
{
    [Params(64, 4096)]
    public int StringLength { get; set; }

    [Params(128, 65536)]
    public int BufferSize { get; set; }

    [Params(false, true)]
    public bool Jsonc { get; set; }

    private byte[] _input = null!;
    private JsonReader _reader = null!;

    [GlobalSetup]
    public void Setup()
    {
        var row = "{\"id\":1,\"text\":\"" + new string('x', StringLength) + "\"";
        row += Jsonc ? ",/* end */},// next\n" : "},";
        var rows = string.Concat(Enumerable.Repeat(row, 1000));
        _input = Encoding.UTF8.GetBytes("[" + (Jsonc ? rows : rows[..^1]) + "]");
        _reader = new JsonReader(new JsonProfile(allowComments: Jsonc, allowTrailingCommas: Jsonc), BufferSize);
        if (ReadRows() != 1000) throw new InvalidOperationException("Incorrect row count.");
    }

    [Benchmark]
    public int ReadRows()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_input));
        var total = 0;
        while (reader.Read()) total += reader.GetInt32(0);
        return total;
    }
}
