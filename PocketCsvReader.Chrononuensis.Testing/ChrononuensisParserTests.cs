using System.Globalization;
using System.Text;
using Chrononuensis;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Chrononuensis.Testing;

public class ChrononuensisParserTests
{
    private static IEnumerable<TestCaseData> Periods()
    {
        yield return new("10-22", new MonthDay(10, 22));
        yield return new("2026-295", new YearDay(2026, 295));
        yield return new("2026-W43", new YearWeek(2026, 43));
        yield return new("2026-10", new YearMonth(2026, 10));
        yield return new("2026-Q4", new YearQuarter(2026, 4));
        yield return new("2026-H2", new YearSemester(2026, 2));
        yield return new("2026", new Year(2026));
        yield return new("I Olympiad", new Olympiad(1));
        yield return new("2020s", new Decade(2020));
        yield return new("21", new Century(21));
    }

    private static CsvDataReader Reader(string input, Type type,
        RuntimeParsersDescriptorBuilder? parsers = null, SchemaDescriptor? schema = null)
    {
        schema ??= new SchemaDescriptorBuilder().Indexed().WithField(type).Build();
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader()
            .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')).Build(),
            schema, new ResourceDescriptorBuilder().WithSequence("NULL", null).Build(),
            (parsers ?? new RuntimeParsersDescriptorBuilder().UseChrononuensis()).Build());
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(input)), profile);
        Assert.That(reader.Read(), Is.True);
        return reader;
    }

    [TestCaseSource(nameof(Periods))]
    public void DefaultPatterns_ParseAllSupportedTypes(string input, object expected)
    {
        using var reader = Reader(input, expected.GetType());
        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(Periods))]
    public void InvalidInput_PreservesFormatException(string _, object expected)
    {
        using var reader = Reader("invalid", expected.GetType());
        Assert.That(() => reader.GetValue(0), Throws.InstanceOf<FormatException>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExplicitRuntimeParser_WinsInEitherOrder(bool integrationFirst)
    {
        var parsers = new RuntimeParsersDescriptorBuilder();
        if (integrationFirst) parsers.UseChrononuensis();
        parsers.WithParser((ReadOnlySpan<char> _) => new YearMonth(2000, 1));
        if (!integrationFirst) parsers.UseChrononuensis();
        using var reader = Reader("ignored", typeof(YearMonth), parsers);
        Assert.That(reader.GetFieldValue<YearMonth>(0), Is.EqualTo(new YearMonth(2000, 1)));
        Assert.That(reader.GetValue(0), Is.EqualTo(new YearMonth(2000, 1)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LegacyRuntimeParser_WinsInEitherOrder(bool integrationFirst)
    {
        var parsers = new RuntimeParsersDescriptorBuilder();
        if (integrationFirst) parsers.UseChrononuensis();
        parsers.WithParser((string _) => new YearMonth(2000, 1));
        if (!integrationFirst) parsers.UseChrononuensis();
        using var reader = Reader("ignored", typeof(YearMonth), parsers);
        Assert.That(reader.GetFieldValue<YearMonth>(0), Is.EqualTo(new YearMonth(2000, 1)));
    }

    [Test]
    public void ExplicitFieldParser_WinsOverFormatAndIntegration()
    {
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithCustomField(typeof(YearMonth), f => f.WithFormat("yyyy-MM")
                .WithParser((ReadOnlySpan<char> _) => new YearMonth(2000, 1))).Build();
        using var reader = Reader("ignored", typeof(YearMonth), schema: schema);
        Assert.That(reader.GetValue(0), Is.EqualTo(new YearMonth(2000, 1)));
        Assert.That(reader.GetFieldValue<YearMonth>(0), Is.EqualTo(new YearMonth(2000, 1)));
    }

    [TestCase("en-US")]
    [TestCase("fr-FR")]
    [TestCase("de-DE")]
    public void FieldPatternAndCulture_MatchPublicSpanApi(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        const string input = "Q4.26";
        const string pattern = "'Q'q.yy";
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithCustomField(typeof(YearQuarter), f => f.WithFormat(pattern, culture)).Build();
        using var reader = Reader(input, typeof(YearQuarter), schema: schema);
        Assert.That(reader.GetFieldValue<YearQuarter>(0),
            Is.EqualTo(YearQuarter.Parse(input.AsSpan(), pattern, culture)));
        Assert.That(reader.GetValue(0), Is.EqualTo(new YearQuarter(2026, 4)));
    }

    [TestCase("2026-13")]
    [TestCase("2026/10")]
    [TestCase("\"\"")]
    public void InvalidTypedValues_PreserveUpstreamErrors(string input)
    {
        using var reader = Reader(input, typeof(YearMonth));
        Assert.That(() => reader.GetFieldValue<YearMonth>(0), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void NullsAndArrays_UseExistingReaderSemantics()
    {
        using var reader = Reader("NULL\r\n[2026-10|2026-11]", typeof(YearMonth));
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(() => reader.GetFieldValue<YearMonth>(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<YearMonth>(0), Is.EqualTo(new[] { new YearMonth(2026, 10), new YearMonth(2026, 11) }));
        Assert.That(reader.GetArray(0), Is.EqualTo(new object[] { new YearMonth(2026, 10), new YearMonth(2026, 11) }));
    }

    [Test]
    public void Registration_IsIdempotentAndDoesNotIncludeOtherTypes()
    {
        var parsers = new RuntimeParsersDescriptorBuilder().UseChrononuensis().UseChrononuensis().Build()!;
        Assert.That(parsers.Count, Is.EqualTo(10));
        using var reader = Reader("42", typeof(int));
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetValue(0), Is.EqualTo(42));
    }

    [Test]
    public void DisabledIntegration_DoesNotRegisterTemporalTypes()
    {
        using var reader = Reader("2026-10", typeof(YearMonth), new RuntimeParsersDescriptorBuilder());
        Assert.That(() => reader.GetFieldValue<YearMonth>(0), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void NullBuilder_IsRejected()
        => Assert.That(() => ChrononuensisParserExtensions.UseChrononuensis(null!), Throws.ArgumentNullException);

    [Test]
    public void FluentReaderConfiguration_UsesIntegrationProvider()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithSchema(s => s.Indexed().WithField<YearMonth>())
            .WithParsers(options => options.UseChrononuensis(culture)).Build();
        using var stream = new MemoryStream("2026-10"u8.ToArray());
        using var reader = csv.ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<YearMonth>(0),
            Is.EqualTo(YearMonth.Parse("2026-10".AsSpan(), culture)));
    }

    [Test]
    public void TemporalFieldFormat_UsesSpanParserForBoxedThenTypedReads()
    {
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithTemporalField(typeof(YearMonth), f => f.WithFormat("MM.yyyy")).Build();
        using var reader = Reader("10.2026", typeof(YearMonth), schema: schema);
        Assert.That(reader.GetValue(0), Is.EqualTo(new YearMonth(2026, 10)));
        Assert.That(reader.GetFieldValue<YearMonth>(0), Is.EqualTo(new YearMonth(2026, 10)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SpanIntegration_AddsNoAllocationBeyondUpstreamParser(bool formatted)
    {
        var schema = formatted ? new SchemaDescriptorBuilder().Indexed()
            .WithCustomField(typeof(YearMonth), f => f.WithFormat("yyyy-MM")).Build() : null;
        using var reader = Reader("2026-10", typeof(YearMonth), schema: schema);
        for (int i = 0; i < 1000; i++)
        {
            reader.GetFieldValue<YearMonth>(0);
            YearMonth.Parse("2026-10".AsSpan(), "yyyy-MM", CultureInfo.InvariantCulture);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            reader.GetFieldValue<YearMonth>(0);
        var integrated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            YearMonth.Parse("2026-10".AsSpan(), "yyyy-MM", CultureInfo.InvariantCulture);
        var direct = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(integrated, Is.EqualTo(direct));
    }
}
