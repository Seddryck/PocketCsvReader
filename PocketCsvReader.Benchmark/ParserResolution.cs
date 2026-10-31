using System.Globalization;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Benchmark;

/// <summary>Separates cached parsing, validation, and first-use delegate construction.</summary>
[MemoryDiagnoser]
public class ParserResolution
{
    private delegate bool TryDate(ReadOnlySpan<char> text, out DateTime value);
    private delegate bool TryFieldDate(int index, ReadOnlySpan<char> text, out DateTime value);

    private CsvDataReader _number = null!;
    private CsvDataReader _date = null!;
    private CsvDataReader _custom = null!;
    private CsvProfile _dateProfile = null!;
    private byte[] _dateBytes = null!;
    private TryDate _tryDate = null!;
    private TryFieldDate _tryFieldDate = null!;

    [GlobalSetup]
    public void Setup()
    {
        var numberSchema = new SchemaDescriptor.IndexedSchemaDescriptor();
        numberSchema.Fields.Add(new FieldDescriptor(typeof(int), Format:
            new NumericFormatDescriptor(NumberStyles.Integer, CultureInfo.InvariantCulture)));
        _number = Open(Encoding.UTF8.GetBytes("12345"), Profile(numberSchema));
        var dateSchema = new SchemaDescriptor.IndexedSchemaDescriptor();
        dateSchema.Fields.Add(new FieldDescriptor(typeof(DateTime), Format:
            new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        _dateProfile = Profile(dateSchema);
        _dateBytes = Encoding.UTF8.GetBytes("2026-02-28");
        _date = Open(_dateBytes, _dateProfile);

        var customSchema = new SchemaDescriptor.IndexedSchemaDescriptor();
        customSchema.Fields.Add(new FieldDescriptor(typeof(DateTime))
        {
            SpanParse = (ParseSpan<DateTime>)(text => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture))
        });
        _custom = Open(_dateBytes, Profile(customSchema));

        // Bind once outside measurement; reflection is not part of validation timing.
        var parserType = typeof(CsvDataReader).Assembly.GetType("PocketCsvReader.SpanParser", throwOnError: true)!;
        var parser = Activator.CreateInstance(parserType)!;
        var method = parserType.GetMethods().Single(m => m.Name == "TryParse" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(DateTime));
        _tryDate = method.CreateDelegate<TryDate>(parser);
        if (FormattedInteger() != 12345 || FormattedDate() != new DateTime(2026, 2, 28)
            || !Equals(BoxedFormattedDate(), new DateTime(2026, 2, 28))
            || CustomSpanDate() != new DateTime(2026, 2, 28) || !ValidDateValidation() || InvalidDateValidation())
            throw new InvalidOperationException("Benchmark inputs did not produce the expected results.");

        var fieldParser = typeof(BaseDataRecord<CsvProfile>).GetProperty("Parser", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_date)!;
        _tryFieldDate = parserType.GetMethods().Single(m => m.Name == "TryParse" && m.GetParameters().Length == 3)
            .MakeGenericMethod(typeof(DateTime)).CreateDelegate<TryFieldDate>(fieldParser);
        if (!ValidFormattedDateValidation() || InvalidFormattedDateValidation())
            throw new InvalidOperationException("Formatted validation produced an unexpected result.");
    }

    private static CsvProfile Profile(SchemaDescriptor schema)
        => new(new DialectDescriptorBuilder().WithoutHeader().Build(), schema);

    private static CsvDataReader Open(byte[] bytes, CsvProfile profile)
    {
        var reader = new CsvDataReader(new MemoryStream(bytes, writable: false), profile);
        if (!reader.Read()) throw new InvalidOperationException("Expected a benchmark row.");
        return reader;
    }

    [Benchmark]
    public int FormattedInteger() => _number.GetInt32(0);

    [Benchmark]
    public DateTime FormattedDate() => _date.GetDateTime(0);

    [Benchmark]
    public object BoxedFormattedDate() => _date.GetValue(0);

    [Benchmark]
    public DateTime CustomSpanDate() => _custom.GetDateTime(0);

    [Benchmark]
    public DateTime FirstFormattedDate()
    {
        using var reader = Open(_dateBytes, _dateProfile);
        return reader.GetDateTime(0);
    }

    [Benchmark]
    public bool ValidDateValidation() => _tryDate("2026-02-28", out _);

    [Benchmark]
    public bool InvalidDateValidation()
    {
        // Baseline TryParse throws; both versions expose false to the benchmark consumer.
        try { return _tryDate("2026-02-31", out _); }
        catch (FormatException) { return false; }
    }

    [Benchmark]
    public bool ValidFormattedDateValidation() => _tryFieldDate(0, "2026-02-28", out _);

    [Benchmark]
    public bool InvalidFormattedDateValidation()
    {
        try { return _tryFieldDate(0, "2026-02-31", out _); }
        catch (FormatException) { return false; }
        catch (TargetInvocationException ex) when (ex.InnerException is FormatException) { return false; }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _number.Dispose();
        _date.Dispose();
        _custom.Dispose();
    }
}
