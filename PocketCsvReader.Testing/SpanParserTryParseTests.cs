using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class SpanParserTryParseTests
{
    [TestCase(false, "42", true, 42)]
    [TestCase(true, "42", true, 42)]
    [TestCase(false, "invalid", false, 0)]
    [TestCase(true, "invalid", false, 0)]
    [TestCase(false, "999999999999999999999", false, 0)]
    [TestCase(true, "999999999999999999999", false, 0)]
    public void TryParse_Conversion_ReturnsResultAndDefaultOnFailure(bool field, string text, bool expected, int value)
    {
        var parser = new SpanParser();
        parser.CacheTypeParser<int>(0);
        int actual;
        Assert.That(field ? parser.TryParse(0, text, out actual) : parser.TryParse(text, out actual), Is.EqualTo(expected));
        Assert.That(actual, Is.EqualTo(value));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TryParse_MissingParser_ReturnsFalseAndDefault(bool field)
    {
        var parser = new SpanParser();
        Version? value;
        Assert.That(field ? parser.TryParse(0, "1.2", out value) : parser.TryParse("1.2", out value), Is.False);
        Assert.That(value, Is.Null);
        // Indexed TryParse checks field registration, not the type-level fallback.
        Assert.That(parser.TryParse<int>(0, "42", out var number), Is.False);
        Assert.That(number, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PairedParser_UsesTryParseAndCachesBothDelegates(bool field)
    {
        var parser = new SpanParser();
        ParseSpan<int> throwing = _ => throw new AssertionException("Parse must not be invoked.");
        TryParseSpan<int> safe = (ReadOnlySpan<char> text, out int value) => int.TryParse(text, out value);
        if (field) parser.Register(0, throwing, safe);
        else parser.Register(throwing, safe);

        TryParseSpan<int>? first;
        TryParseSpan<int>? second;
        Assert.That(field ? parser.TryGetTryParser(0, out first) : parser.TryGetTryParser(out first), Is.True);
        Assert.That(field ? parser.TryGetTryParser(0, out second) : parser.TryGetTryParser(out second), Is.True);
        Assert.That(first, Is.SameAs(safe));
        Assert.That(second, Is.SameAs(first));
        foreach (var text in new[] { "42", "invalid", "43" })
        {
            int value;
            Assert.That(field ? parser.TryParse(0, text, out value) : parser.TryParse(text, out value), Is.EqualTo(text != "invalid"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedTryParser_DiscardsItsOutput(bool field)
    {
        var parser = new SpanParser();
        TryParseSpan<int> fail = (ReadOnlySpan<char> _, out int value) => { value = 99; return false; };
        if (field) parser.Register<int>(0, _ => 99, fail);
        else parser.Register<int>(_ => 99, fail);
        int actual;
        Assert.That(field ? parser.TryParse(0, "x", out actual) : parser.TryParse("x", out actual), Is.False);
        Assert.That(actual, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ParseOnlyFallback_CachesDelegateAndReportsConversionFailures(bool field, bool wrapped)
    {
        var parser = new SpanParser();
        ParseSpan<int> parse = _ => throw (wrapped
            ? new TargetInvocationException(new FormatException()) : new FormatException());
        if (field) parser.Register(0, parse);
        else parser.Register(parse);
        TryParseSpan<int>? first;
        TryParseSpan<int>? second;
        Assert.That(field ? parser.TryGetTryParser(0, out first) : parser.TryGetTryParser(out first), Is.True);
        Assert.That(field ? parser.TryGetTryParser(0, out second) : parser.TryGetTryParser(out second), Is.True);
        Assert.That(second, Is.SameAs(first));
        int value;
        Assert.That(field ? parser.TryParse(0, "bad", out value) : parser.TryParse("bad", out value), Is.False);
        Assert.That(value, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParseOnlyFallback_DoesNotHideUnexpectedFailures(bool field)
    {
        var parser = new SpanParser();
        var error = new InvalidOperationException("Parser configuration failed.");
        ParseSpan<int> parse = _ => throw error;
        if (field) parser.Register(0, parse);
        else parser.Register(parse);
        Assert.That(() => { if (field) parser.TryParse<int>(0, "x", out _); else parser.TryParse<int>("x", out _); },
            Throws.Exception.SameAs(error));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Replacement_RefreshesTypedTryAndBoxedCaches(bool field)
    {
        var parser = new SpanParser();
        ParseSpan<int> oldParse = _ => 1;
        TryParseSpan<int> oldTry = (ReadOnlySpan<char> _, out int value) => { value = 1; return true; };
        if (field) parser.Register(0, typeof(int), oldParse, oldTry);
        else parser.Register(typeof(int), oldParse, oldTry);
        TryParseSpan<int>? previousTry;
        ParseSpan<object>? previousBox;
        Assert.That(field ? parser.TryGetTryParser(0, out previousTry) : parser.TryGetTryParser(out previousTry), Is.True);
        Assert.That(field ? parser.TryGetParser(0, out previousBox) : parser.TryGetParser(typeof(int), out previousBox), Is.True);

        ParseSpan<int> newParse = _ => 2;
        TryParseSpan<int> newTry = (ReadOnlySpan<char> _, out int value) => { value = 2; return true; };
        if (field) parser.Register(0, typeof(int), newParse, newTry);
        else parser.Register(typeof(int), newParse, newTry);
        TryParseSpan<int>? currentTry;
        ParseSpan<object>? currentBox;
        ParseSpan<object>? repeatedBox;
        Assert.That(field ? parser.TryGetTryParser(0, out currentTry) : parser.TryGetTryParser(out currentTry), Is.True);
        Assert.That(currentTry, Is.SameAs(newTry).And.Not.SameAs(previousTry));
        Assert.That(field ? parser.TryGetParser(0, out currentBox) : parser.TryGetParser(typeof(int), out currentBox), Is.True);
        Assert.That(field ? parser.TryGetParser(0, out repeatedBox) : parser.TryGetParser(typeof(int), out repeatedBox), Is.True);
        Assert.That(currentBox, Is.Not.SameAs(previousBox));
        Assert.That(repeatedBox, Is.SameAs(currentBox));
        Assert.That(currentBox!(""), Is.EqualTo(2));
        Assert.That(currentTry!("", out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(2));

        // Replacing a pair with Parse-only registration must discard the old TryParse too.
        if (field) parser.Register(0, newParse);
        else parser.Register(newParse);
        TryParseSpan<int>? fallback;
        Assert.That(field ? parser.TryGetTryParser(0, out fallback) : parser.TryGetTryParser(out fallback), Is.True);
        Assert.That(fallback, Is.Not.SameAs(newTry));
        Assert.That(fallback!("", out actual), Is.True);
        Assert.That(actual, Is.EqualTo(2));
    }

    [Test]
    public void FieldFailure_DoesNotUseTypeParser()
    {
        var parser = new SpanParser();
        parser.Register<int>(_ => 42);
        parser.Register<int>(0, _ => throw new FormatException());
        Assert.That(parser.TryParse<int>(0, "42", out var value), Is.False);
        Assert.That(value, Is.Zero);
        Assert.That(() => parser.Parse<int>(0, "42"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void BuiltInTemporalParsers_RejectImpossibleDates()
    {
        var parser = new SpanParser();
        Assert.That(parser.TryParse<DateTime>("2026-02-31", out _), Is.False);
        Assert.That(parser.TryParse<DateOnly>("2026-02-31", out _), Is.False);
        Assert.That(parser.TryParse<DateTimeOffset>("2026-02-31", out _), Is.False);
        Assert.That(parser.TryParse<TimeOnly>("25:00", out _), Is.False);
        Assert.That(parser.TryParse<char>("", out _), Is.False);
    }

    [Test]
    public void FormattedParser_PairsExactOverloadWithoutInvokingParse()
    {
        var locator = new TypeParserLocator<TryOnlyDate>();
        var safe = (TryParseSpan<TryOnlyDate>)locator.LocateTryParser(["yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None])!;
        Assert.That(safe("2026-02-28", out var value), Is.True);
        Assert.That(value.Value, Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(safe("2026-02-31", out _), Is.False);
        Assert.That(safe("02/28/2026", out _), Is.False);
    }

    public readonly record struct TryOnlyDate(DateTime Value)
    {
        public static TryOnlyDate ParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles)
            => throw new AssertionException("ParseExact must not be invoked during validation.");

        public static bool TryParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles, out TryOnlyDate value)
        {
            var success = DateTime.TryParseExact(text, pattern, culture, styles, out var date);
            value = new(date);
            return success;
        }
    }

    public readonly record struct TryOnlyNumber(int Value)
    {
        public static TryOnlyNumber Parse(ReadOnlySpan<char> text)
            => throw new AssertionException("Direct Parse must not be invoked during validation.");

        public static bool TryParse(ReadOnlySpan<char> text, out TryOnlyNumber value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirectStaticParser_DiscoversAndCachesMatchingTryParse(bool field)
    {
        var parser = new SpanParser();
        if (field) parser.Register<TryOnlyNumber>(0, TryOnlyNumber.Parse);
        else parser.Register<TryOnlyNumber>(TryOnlyNumber.Parse);
        TryParseSpan<TryOnlyNumber>? first;
        TryParseSpan<TryOnlyNumber>? repeated;
        Assert.That(field ? parser.TryGetTryParser(0, out first) : parser.TryGetTryParser(out first), Is.True);
        Assert.That(field ? parser.TryGetTryParser(0, out repeated) : parser.TryGetTryParser(out repeated), Is.True);
        Assert.That(repeated, Is.SameAs(first));
        TryOnlyNumber actual;
        Assert.That(field ? parser.TryParse(0, "42", out actual) : parser.TryParse("42", out actual), Is.True);
        Assert.That(actual.Value, Is.EqualTo(42));
        Assert.That(field ? parser.TryParse(0, "bad", out actual) : parser.TryParse("bad", out actual), Is.False);
        Assert.That(actual, Is.Default);
    }

    [Test]
    public void CachedTypeBinding_ReusesTryParserUntilExplicitRebinding()
    {
        var parser = new SpanParser();
        Assert.That(parser.TryGetTryParser<int>(out var original), Is.True);
        parser.CacheTypeParser<int>(0);
        Assert.That(parser.TryGetTryParser<int>(0, out var bound), Is.True);
        Assert.That(bound, Is.SameAs(original));
        TryParseSpan<int> replacement = (ReadOnlySpan<char> _, out int value) => { value = 99; return true; };
        parser.Register<int>(_ => 99, replacement);
        Assert.That(parser.TryGetTryParser<int>(0, out var snapshot), Is.True);
        Assert.That(snapshot, Is.SameAs(original));
        parser.CacheTypeParser<int>(0);
        Assert.That(parser.TryGetTryParser<int>(0, out var rebound), Is.True);
        Assert.That(rebound, Is.SameAs(replacement));
        parser.ClearFieldParsers();
        Assert.That(parser.TryGetTryParser<int>(0, out _), Is.False);
        Assert.That(parser.TryGetTryParser<int>(out var preserved), Is.True);
        Assert.That(preserved, Is.SameAs(replacement));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParseOnlyFallback_HandlesOverflowAndInvalidCast(bool overflow)
    {
        var parser = new SpanParser();
        parser.Register<int>(_ => throw (overflow ? new OverflowException() : new InvalidCastException()));
        Assert.That(parser.TryParse<int>("x", out var value), Is.False);
        Assert.That(value, Is.Zero);
    }

    [Test]
    public void PairedParser_PropagatesUnexpectedFailure()
    {
        var parser = new SpanParser();
        parser.Register<int>(_ => 42, (ReadOnlySpan<char> _, out int value) => throw new InvalidOperationException("Broken parser"));
        Assert.That(() => parser.TryParse<int>("42", out _), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => parser.Register(typeof(int), (ParseSpan<int>)(_ => 42),
            (TryParseSpan<long>)((ReadOnlySpan<char> _, out long value) => { value = 42; return true; })),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void FormattedNumericParser_UsesConfiguredStyleAndCulture()
    {
        var locator = new TypeParserLocator<decimal>();
        var safe = (TryParseSpan<decimal>)locator.LocateTryParser([NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR")])!;
        Assert.That(safe("1,25", out var value), Is.True);
        Assert.That(value, Is.EqualTo(1.25m));
        Assert.That(safe("1.25", out _), Is.False);
        var fallbackLocator = new TypeParserLocator<int>();
        var integer = (TryParseSpan<int>)fallbackLocator.LocateTryParser(["unused", CultureInfo.InvariantCulture, DateTimeStyles.None])!;
        Assert.That(integer("42", out var number), Is.True);
        Assert.That(number, Is.EqualTo(42));
    }

    private static SpanParser GetParser(CsvDataReader reader)
        => (SpanParser)typeof(BaseDataRecord<CsvProfile>).GetProperty("Parser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;

    [Test]
    public void Reader_FormattedCachesPersistAcrossRowsAndStaySeparateByColumn()
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime), Format: new TemporalFormatDescriptor("dd MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"))));
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime), Format: new TemporalFormatDescriptor("MMMM dd, yyyy", CultureInfo.GetCultureInfo("en-US"))));
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithDelimiter(';').WithoutHeader().Build(), schema);
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(
            "28 février 2026;February 28, 2026\r\n31 février 2026;February 31, 2026")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetDateTime(0), Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(reader.GetValue(1), Is.EqualTo(new DateTime(2026, 2, 28)));
        var parser = GetParser(reader);
        Assert.That(parser.TryGetTryParser<DateTime>(0, out var first), Is.True);
        Assert.That(parser.TryGetTryParser<DateTime>(1, out var second), Is.True);
        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first!("28 février 2026", out _), Is.True);
        Assert.That(first("February 28, 2026", out _), Is.False);
        Assert.That(second!("February 28, 2026", out _), Is.True);
        Assert.That(parser.TryGetParser(0, out var boxed), Is.True);

        Assert.That(reader.Read(), Is.True);
        Assert.That(parser.TryGetTryParser<DateTime>(0, out var repeated), Is.True);
        Assert.That(repeated, Is.SameAs(first));
        Assert.That(parser.TryGetParser(0, out var repeatedBoxed), Is.True);
        Assert.That(repeatedBoxed, Is.SameAs(boxed));
        Assert.That(parser.TryParse<DateTime>(0, reader.GetString(0), out _), Is.False);
        Assert.That(parser.TryParse<DateTime>(1, reader.GetString(1), out _), Is.False);
        Assert.That(() => reader.GetDateTime(0), Throws.TypeOf<FormatException>());
        Assert.That(() => reader.GetValue(1), Throws.TypeOf<FormatException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Reader_CachedCustomFailure_DoesNotFallbackOrRetry(bool legacy)
    {
        var calls = 0;
        int Parse(ReadOnlySpan<char> text)
        {
            calls++;
            if (text.SequenceEqual("42")) throw new FormatException();
            return int.Parse(text);
        }
        var schema = new SchemaDescriptorBuilder().Indexed();
        if (legacy) schema.WithField<int>(f => f.WithParser((string text) => Parse(text.AsSpan())));
        else schema.WithField<int>(f => f.WithParser((ParseSpan<int>)Parse));
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema.Build());
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("41\r\n42")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(41));
        Assert.That(reader.Read(), Is.True);
        var before = calls;
        Assert.That(() => reader.GetInt32(0), Throws.TypeOf<FormatException>());
        Assert.That(calls, Is.EqualTo(before + 1));
        Assert.That(() => reader.GetFieldValue<int>(0), Throws.TypeOf<FormatException>());
        Assert.That(calls, Is.EqualTo(before + 2));
    }

    [Test]
    public void NullableFormattedParser_UsesLiftedTryParse()
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime?), Format: new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema);
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("2026-02-28")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(new DateTime(2026, 2, 28)));
        var parser = GetParser(reader);
        Assert.That(parser.TryParse<DateTime?>(0, "2026-02-31", out var value), Is.False);
        Assert.That(value, Is.Null);
        Assert.That(parser.TryParse<DateTime?>(0, "2026-02-28", out value), Is.True);
        Assert.That(value, Is.EqualTo(new DateTime(2026, 2, 28)));
    }
}
