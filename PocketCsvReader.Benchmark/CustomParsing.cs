using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CustomParsing
{
    [Params(false, true)]
    public bool FieldParser { get; set; }

    private CsvDataReader _stringNumber = null!, _spanNumber = null!;
    private CsvDataReader _stringDate = null!, _spanDate = null!;

    [GlobalSetup]
    public void Setup()
    {
        _stringNumber = Create("12345", (string s) => int.Parse(s, CultureInfo.InvariantCulture), (ReadOnlySpan<char> s) => int.Parse(s, CultureInfo.InvariantCulture), false);
        _spanNumber = Create("12345", (string s) => int.Parse(s, CultureInfo.InvariantCulture), (ReadOnlySpan<char> s) => int.Parse(s, CultureInfo.InvariantCulture), true);
        _stringDate = Create("2026-10-22", (string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture), (ReadOnlySpan<char> s) => DateOnly.Parse(s, CultureInfo.InvariantCulture), false);
        _spanDate = Create("2026-10-22", (string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture), (ReadOnlySpan<char> s) => DateOnly.Parse(s, CultureInfo.InvariantCulture), true);
        // Measure steady-state parsing, excluding registration and first-read caches.
        StringNumber(); SpanNumber(); StringDate(); SpanDate();
    }

    private CsvDataReader Create<T>(string text, ParseFunction<T> legacy, ParseSpan<T> span, bool useSpan)
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        var parsers = new RuntimeParsersDescriptor();
        var field = new FieldDescriptor(typeof(T));
        if (FieldParser)
            field = useSpan ? field with { SpanParse = span } : field with { Parse = s => legacy(s)! };
        else if (useSpan)
            parsers.AddParser(span);
        else
            parsers.AddParser(legacy);
        schema.Fields.Add(field);
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema, null, parsers);
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), profile);
        reader.Read();
        return reader;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Number")]
    public int StringNumber() => _stringNumber.GetFieldValue<int>(0);
    [Benchmark, BenchmarkCategory("Number")]
    public int SpanNumber() => _spanNumber.GetFieldValue<int>(0);
    [Benchmark(Baseline = true), BenchmarkCategory("Date")]
    public DateOnly StringDate() => _stringDate.GetFieldValue<DateOnly>(0);
    [Benchmark, BenchmarkCategory("Date")]
    public DateOnly SpanDate() => _spanDate.GetFieldValue<DateOnly>(0);

    [GlobalCleanup]
    public void Cleanup()
    {
        _stringNumber.Dispose(); _spanNumber.Dispose();
        _stringDate.Dispose(); _spanDate.Dispose();
    }
}
