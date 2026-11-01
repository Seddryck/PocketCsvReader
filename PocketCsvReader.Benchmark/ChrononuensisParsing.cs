using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Chrononuensis;
using PocketCsvReader.Chrononuensis;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ChrononuensisParsing
{
    private CsvDataReader _existing = null!;
    private CsvDataReader _legacy = null!;
    private CsvDataReader _integration = null!;
    private CsvDataReader _number = null!;
    private CsvDataReader _numberEnabled = null!;

    [GlobalSetup]
    public void Setup()
    {
        _existing = Create<YearMonth>("2026-10", new RuntimeParsersDescriptorBuilder(), true);
        _legacy = Create<YearMonth>("2026-10", new RuntimeParsersDescriptorBuilder()
            .WithParser((string input) => YearMonth.Parse(input, CultureInfo.InvariantCulture)));
        _integration = Create<YearMonth>("2026-10", new RuntimeParsersDescriptorBuilder().UseChrononuensis());
        _number = Create<int>("12345", new RuntimeParsersDescriptorBuilder());
        _numberEnabled = Create<int>("12345", new RuntimeParsersDescriptorBuilder().UseChrononuensis());
        ExistingFormatted(); LegacyString(); IntegrationSpan(); NumberDisabled(); NumberEnabled();
    }

    private static CsvDataReader Create<T>(string value, RuntimeParsersDescriptorBuilder parsers, bool formatted = false)
    {
        var schema = new SchemaDescriptorBuilder().Indexed();
        if (formatted) schema.WithCustomField(typeof(T), f => f.WithFormat("yyyy-MM"));
        else schema.WithField<T>();
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema.Build(), null, parsers.Build());
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(value)), profile);
        reader.Read();
        return reader;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Temporal")]
    public YearMonth ExistingFormatted() => _existing.GetFieldValue<YearMonth>(0);
    [Benchmark]
    [BenchmarkCategory("Temporal")]
    public YearMonth LegacyString() => _legacy.GetFieldValue<YearMonth>(0);
    [Benchmark]
    [BenchmarkCategory("Temporal")]
    public YearMonth IntegrationSpan() => _integration.GetFieldValue<YearMonth>(0);
    [Benchmark]
    [BenchmarkCategory("Temporal")]
    public YearMonth DirectSpan() => YearMonth.Parse("2026-10".AsSpan(), CultureInfo.InvariantCulture);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Unaffected")]
    public int NumberDisabled() => _number.GetFieldValue<int>(0);
    [Benchmark]
    [BenchmarkCategory("Unaffected")]
    public int NumberEnabled() => _numberEnabled.GetFieldValue<int>(0);

    [GlobalCleanup]
    public void Cleanup()
    {
        _existing.Dispose(); _legacy.Dispose(); _integration.Dispose();
        _number.Dispose(); _numberEnabled.Dispose();
    }
}
