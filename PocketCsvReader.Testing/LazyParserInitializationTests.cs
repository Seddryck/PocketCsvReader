using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class LazyParserInitializationTests
{
    private static object? State(SpanParser.ParserEntry entry, string field)
        => typeof(SpanParser.ParserEntry).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry);

    private static SpanParser.ParserEntry SchemaEntry(BaseDataRecord<CsvProfile> record)
        => ((IDictionary)typeof(BaseDataRecord<CsvProfile>).GetField("_schemaParsers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(record)!).Values.Cast<SpanParser.ParserEntry>().Single();

    [TestCase(false)]
    [TestCase(true)]
    public void ResolutionFactory_IsLazyAndCachesSuccessOrMissingTryParse(bool missing)
    {
        var calls = 0;
        ParseSpan<int> parse = text => int.Parse(text);
        TryParseSpan<int> safe = (ReadOnlySpan<char> text, out int value) => int.TryParse(text, out value);
        var entry = new SpanParser.ParserEntry(parse, tryFactory: () => { calls++; return missing ? null : safe; });
        Assert.That(((ParseSpan<int>)entry.Typed)("42"), Is.EqualTo(42));
        Assert.That(calls, Is.Zero);
        Assert.That(State(entry, "_boxed"), Is.Null);
        Assert.That(State(entry, "_tryParse"), Is.Null);
        Assert.That(entry.Boxed("42"), Is.EqualTo(42));
        Assert.That(calls, Is.Zero);

        var first = entry.GetTryParser<int>();
        Assert.That(first("42", out var value), Is.True);
        Assert.That(value, Is.EqualTo(42));
        Assert.That(first("invalid", out value), Is.False);
        Assert.That(value, Is.Zero);
        Assert.That(entry.GetTryParser<int>(), Is.SameAs(first));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(State(entry, "_tryFactory"), Is.Null, "Release the resolution plan after caching the delegate or fallback.");
    }

    [Test]
    public void ValidationInitialization_DoesNotInitializeBoxing()
    {
        var entry = new TypeParserLocator<DateTime>().CreateEntry(typeof(DateTime),
            ["yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None]);
        Assert.That(State(entry, "_tryParse"), Is.Null);
        var safe = entry.GetTryParser<DateTime>();
        Assert.That(safe("2026-02-28", out _), Is.True);
        Assert.That(safe("2026-02-31", out _), Is.False);
        Assert.That(State(entry, "_boxed"), Is.Null);
        Assert.That(entry.GetTryParser<DateTime>(), Is.SameAs(safe));
        var boxed = entry.Boxed;
        Assert.That(entry.Boxed, Is.SameAs(boxed));
    }

    [Test]
    public void ResolutionPlan_SnapshotsOptionsBeforeDeferredValidation()
    {
        object[] options = ["dd MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None];
        var entry = new TypeParserLocator<DateTime>().CreateEntry(typeof(DateTime), options);
        options[0] = "MMMM dd, yyyy";
        options[1] = CultureInfo.GetCultureInfo("en-US");
        Assert.That(((ParseSpan<DateTime>)entry.Typed)("28 février 2026"), Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(State(entry, "_tryParse"), Is.Null);
        var safe = entry.GetTryParser<DateTime>();
        Assert.That(safe("28 février 2026", out _), Is.True);
        Assert.That(safe("February 28, 2026", out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Reader_TypedAccessDefersValidationAndBoxing(bool generic)
    {
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime), Format:
            new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("2026-02-28\r\n2026-02-27")),
            new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema));
        Assert.That(reader.Read(), Is.True);
        Assert.That(generic ? reader.GetFieldValue<DateTime>(0) : reader.GetDateTime(0), Is.EqualTo(new DateTime(2026, 2, 28)));
        var entry = SchemaEntry(reader);
        Assert.That(State(entry, "_boxed"), Is.Null);
        Assert.That(State(entry, "_tryParse"), Is.Null);
        Assert.That(State(entry, "_tryFactory"), Is.Not.Null);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<DateTime>(0), Is.EqualTo(new DateTime(2026, 2, 27)));
        Assert.That(SchemaEntry(reader), Is.SameAs(entry));
        Assert.That(State(entry, "_boxed"), Is.Null);
        Assert.That(reader.GetValue(0), Is.EqualTo(new DateTime(2026, 2, 27)));
        var boxed = State(entry, "_boxed");
        Assert.That(boxed, Is.Not.Null);
        Assert.That(State(entry, "_tryParse"), Is.Null);
        reader.GetValue(0);
        Assert.That(State(entry, "_boxed"), Is.SameAs(boxed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GenericRegistrations_DeferTheirBoxedAdapters(bool field)
    {
        var parser = new SpanParser();
        if (field) parser.Register<int>(0, text => int.Parse(text));
        else parser.Register<int>(text => int.Parse(text));
        var dictionary = (IDictionary)typeof(SpanParser).GetProperty(field ? "FieldParsers" : "TypeParsers",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(parser)!;
        var entry = (SpanParser.ParserEntry)dictionary[field ? (object)0 : typeof(int)]!;
        ParseSpan<int>? typed;
        Assert.That(field ? parser.TryGetParser(0, out typed) : parser.TryGetParser(out typed), Is.True);
        Assert.That(typed!("42"), Is.EqualTo(42));
        Assert.That(State(entry, "_boxed"), Is.Null);
        ParseSpan<object>? boxed;
        Assert.That(field ? parser.TryGetParser(0, out boxed) : parser.TryGetParser(typeof(int), out boxed), Is.True);
        Assert.That(boxed!("42"), Is.EqualTo(42));
        Assert.That(State(entry, "_boxed"), Is.SameAs(boxed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplacementAndRebinding_KeepLazyStateWithItsOwnEntry(bool initializeFirst)
    {
        var oldCalls = 0;
        var newCalls = 0;
        var oldEntry = new SpanParser.ParserEntry((ParseSpan<int>)(_ => 1), tryFactory: () => { oldCalls++; return null; });
        var newEntry = new SpanParser.ParserEntry((ParseSpan<int>)(_ => 2), tryFactory: () => { newCalls++; return null; });
        var parser = new SpanParser();
        parser.BindFieldParser(0, oldEntry);
        if (initializeFirst) Assert.That(parser.TryParse<int>(0, "", out _), Is.True);
        parser.ClearFieldParsers();
        parser.BindFieldParser(1, oldEntry);
        Assert.That(parser.TryParse<int>(1, "", out var original), Is.True);
        Assert.That(original, Is.EqualTo(1));
        Assert.That(oldCalls, Is.EqualTo(1));
        parser.BindFieldParser(1, newEntry);
        Assert.That(newCalls, Is.Zero);
        Assert.That(parser.TryParse<int>(1, "", out var replacement), Is.True);
        Assert.That(replacement, Is.EqualTo(2));
        Assert.That(newCalls, Is.EqualTo(1));
        Assert.That(oldCalls, Is.EqualTo(1));
        Assert.That(parser.TryParse<int>(1, "", out _), Is.True);
        Assert.That(newCalls, Is.EqualTo(1));
    }

    [Test]
    public void NullablePlan_DefersLiftedValidationAndPreservesFailureOutput()
    {
        var entry = new TypeParserLocator<DateTime>().CreateEntry(typeof(DateTime?),
            ["yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None]);
        Assert.That(((ParseSpan<DateTime?>)entry.Typed)("2026-02-28"), Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(State(entry, "_tryParse"), Is.Null);
        var safe = entry.GetTryParser<DateTime?>();
        Assert.That(safe("2026-02-31", out var value), Is.False);
        Assert.That(value, Is.Null);
        Assert.That(safe("2026-02-28", out value), Is.True);
        Assert.That(value, Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(State(entry, "_boxed"), Is.Null);
        Assert.That(entry.GetTryParser<DateTime?>(), Is.SameAs(safe));
    }

    [Test]
    public void DeferredFactoryFailure_IsVisibleAndDoesNotPoisonTypedReads()
    {
        var entry = new SpanParser.ParserEntry((ParseSpan<int>)(_ => 42),
            tryFactory: () => throw new InvalidOperationException("Invalid parser configuration"));
        Assert.That(() => entry.GetTryParser<int>(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(State(entry, "_tryParse"), Is.Null);
        Assert.That(((ParseSpan<int>)entry.Typed)(""), Is.EqualTo(42));
        Assert.That(entry.Boxed(""), Is.EqualTo(42));
    }
}
