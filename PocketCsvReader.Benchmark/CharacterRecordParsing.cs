using System.Text;
using BenchmarkDotNet.Attributes;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[ShortRunJob]
public class CharacterRecordParsing
{
    [Params(32, 4096, 32768)]
    public int FieldLength { get; set; }
    private byte[] _data = [];
    private CsvProfile _profile = null!;

    [GlobalSetup]
    public void Setup()
    {
        _data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(new string('x', FieldLength) + ",42\r\n", 128)));
        _profile = new(new(Header: false)) { ParserOptimizations = new(BufferSize: 1024, ReadAhead: false) };
    }

    [Benchmark]
    public int Sync()
    {
        using var stream = new MemoryStream(_data);
        using var text = new StreamReader(stream);
        using var parser = new RecordParser(text, _profile);
        int count = 0;
        bool eof;
        do
        {
            eof = parser.IsEndOfFile(out var record, out _);
            count += record.FieldSpans?.Length ?? 0;
        } while (!eof);
        return count;
    }

    [Benchmark]
    public async Task<int> Async()
    {
        using var stream = new MemoryStream(_data);
        using var text = new StreamReader(stream);
        using var parser = new RecordParser(text, _profile);
        int count = 0;
        RecordReadResult result;
        do
        {
            result = await parser.ReadAsync();
            count += result.Record.FieldSpans.Length;
        } while (!result.IsEndOfFile);
        return count;
    }
}
