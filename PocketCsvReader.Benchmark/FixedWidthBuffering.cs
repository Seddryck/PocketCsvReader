using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class FixedWidthBuffering
{
    private const int RecordCount = 512;
    private byte[] _data = [];
    private FixedWidthReader _reader = null!;

    [Params(32, 8192)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\r\n")
            .WithField("Id", 0, 4, FixedWidthPadding.None)
            .WithField("Text", 4, Width - 4, FixedWidthPadding.None)
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1024))
            .Build();
        // Include a final unterminated record and UTF-8 text; all rows are consumed.
        var row = "0042" + new string('é', Width - 4);
        _data = Encoding.UTF8.GetBytes(string.Join("\r\n", Enumerable.Repeat(row, RecordCount)));
        if (Rows() != RecordCount || Typed() != RecordCount * 42L || RowsAsync().GetAwaiter().GetResult() != RecordCount)
            throw new InvalidOperationException("Unexpected fixed-width benchmark output.");
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
        using var reader = _reader.ToDataReader(new MemoryStream(_data, writable: false));
        long sum = 0;
        while (reader.Read()) sum += reader.GetInt32(0);
        return sum;
    }

    [Benchmark]
    public async Task<int> RowsAsync()
    {
        using var reader = _reader.ToDataReader(new MemoryStream(_data, writable: false));
        var count = 0;
        while (await reader.ReadAsync()) count++;
        return count;
    }
}
