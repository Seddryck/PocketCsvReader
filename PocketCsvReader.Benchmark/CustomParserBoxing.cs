using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

/// <summary>Isolates generic string-parser boxing from input materialization and reader setup.</summary>
[MemoryDiagnoser]
public class CustomParserBoxing
{
    public readonly record struct Amount(long Value);

    [Params(false, true)]
    public bool UseBuilder { get; set; }

    private CsvDataReader _integer = null!;
    private CsvDataReader _wide = null!;
    private CsvDataReader _amount = null!;
    private CsvDataReader _text = null!;
    private CsvDataReader _span = null!;

    [GlobalSetup]
    public void Setup()
    {
        _integer = Create<int>(s => int.Parse(s, CultureInfo.InvariantCulture));
        _wide = Create<long>(s => long.Parse(s, CultureInfo.InvariantCulture));
        _amount = Create<Amount>(s => new(long.Parse(s, CultureInfo.InvariantCulture)));
        _text = Create<string>(s => s);
        var spanParsers = new RuntimeParsersDescriptor();
        spanParsers.AddParser<int>((ReadOnlySpan<char> s) => int.Parse(s, CultureInfo.InvariantCulture));
        _span = CreateReader(typeof(int), spanParsers);
        // Warm both typed and boxed lazy adapters before measurement.
        if (TypedInt32() != 3000000 || TypedInt64() != 3000000L || TypedStruct().Value != 3000000L
            || (int)BoxedInt32() != 3000000 || StringInputControl() != 7 || SpanControl() != 3000000)
            throw new InvalidOperationException("Unexpected parser result.");
    }

    private CsvDataReader Create<T>(ParseFunction<T> parse)
    {
        RuntimeParsersDescriptor parsers;
        if (UseBuilder)
        {
            parsers = new RuntimeParsersDescriptorBuilder().WithParser(parse).Build()!;
        }
        else
        {
            parsers = new RuntimeParsersDescriptor();
            parsers.AddParser(parse);
        }
        return CreateReader(typeof(T), parsers);
    }

    private static CsvDataReader CreateReader(Type type, RuntimeParsersDescriptor parsers)
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(type));
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema, null, parsers);
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("3000000")), profile);
        if (!reader.Read()) throw new InvalidOperationException("Missing benchmark row.");
        return reader;
    }

    [Benchmark]
    public int TypedInt32() => _integer.GetInt32(0);

    [Benchmark]
    public long TypedInt64() => _wide.GetInt64(0);

    [Benchmark]
    public Amount TypedStruct() => _amount.GetFieldValue<Amount>(0);

    [Benchmark]
    public object BoxedInt32() => _integer.GetValue(0);

    [Benchmark]
    public int StringInputControl() => _text.GetFieldValue<string>(0).Length;

    [Benchmark]
    public int SpanControl() => _span.GetInt32(0);

    [GlobalCleanup]
    public void Cleanup()
    {
        _integer.Dispose();
        _wide.Dispose();
        _amount.Dispose();
        _text.Dispose();
        _span.Dispose();
    }
}
