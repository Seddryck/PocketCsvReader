using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class SpanParserCachingTest
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RepeatedObjectLookup_ReusesWrapperAndKeepsTypedParser(bool field, bool runtimeRegistration)
    {
        var parser = new SpanParser();
        ParseSpan<int> typed = span => int.Parse(span) + 1;
        Register(parser, typed, field, runtimeRegistration);

        var first = Resolve(parser, field);
        var second = Resolve(parser, field);

        Assert.That(second, Is.SameAs(first));
        Assert.That(first("41"), Is.EqualTo(42));
        ParseSpan<int>? actual;
        Assert.That(field ? parser.TryGetParser(0, out actual) : parser.TryGetParser(out actual), Is.True);
        Assert.That(actual, Is.SameAs(typed));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ReplacingParser_ReplacesBoxedWrapper(bool field, bool runtimeRegistration)
    {
        var parser = new SpanParser();
        Register(parser, span => int.Parse(span) + 1, field, runtimeRegistration);
        var previous = Resolve(parser, field);
        Register(parser, span => int.Parse(span) + 2, field, runtimeRegistration);
        var current = Resolve(parser, field);

        Assert.That(current, Is.Not.SameAs(previous));
        Assert.That(previous("40"), Is.EqualTo(41));
        Assert.That(current("40"), Is.EqualTo(42));
        Assert.That(Resolve(parser, field), Is.SameAs(current));
    }

    [Test]
    public void DifferentFieldsOfSameType_KeepTheirOwnParsers()
    {
        var parser = new SpanParser();
        parser.Register<int>(0, span => int.Parse(span) + 1);
        parser.Register(1, typeof(int), (ParseSpan<int>)(span => int.Parse(span) + 2));
        Assert.That(parser.TryGetParser(0, out var first), Is.True);
        Assert.That(parser.TryGetParser(1, out var second), Is.True);
        Assert.That(first!("40"), Is.EqualTo(41));
        Assert.That(second!("40"), Is.EqualTo(42));
    }

    [Test]
    public void ClearFieldParsers_PreservesTypeCacheAndAllowsNewFieldBinding()
    {
        var parser = new SpanParser();
        parser.Register<int>(span => int.Parse(span) + 1);
        var typeParser = Resolve(parser, false);
        parser.CacheTypeParser<int>(0);
        Assert.That(Resolve(parser, true), Is.SameAs(typeParser));

        parser.ClearFieldParsers();
        Assert.That(parser.TryGetParser(0, out _), Is.False);
        Assert.That(Resolve(parser, false), Is.SameAs(typeParser));
        parser.Register<int>(0, span => int.Parse(span) + 2);
        Assert.That(Resolve(parser, true)("40"), Is.EqualTo(42));
    }

    [Test]
    public void CachedFieldBinding_RemainsUntilExplicitlyRebound()
    {
        var parser = new SpanParser();
        parser.Register<int>(span => int.Parse(span) + 1);
        parser.CacheTypeParser<int>(0);
        var fieldParser = Resolve(parser, true);
        parser.Register<int>(span => int.Parse(span) + 2);
        Assert.That(Resolve(parser, true), Is.SameAs(fieldParser));
        parser.CacheTypeParser<int>(0);
        Assert.That(Resolve(parser, true), Is.SameAs(Resolve(parser, false)));
        Assert.That(Resolve(parser, true)("40"), Is.EqualTo(42));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NullableAndReferenceParsers_PreserveNullsAndBoxedValues(bool runtimeRegistration)
    {
        var parser = new SpanParser();
        ParseSpan<int?> nullable = span => span.IsEmpty ? null : int.Parse(span);
        ParseSpan<string?> reference = span => span.IsEmpty ? null : span.ToString();
        if (runtimeRegistration)
        {
            parser.Register(typeof(int?), nullable);
            parser.Register(typeof(string), reference);
        }
        else
        {
            parser.Register(nullable);
            parser.Register(reference);
        }
        Assert.That(parser.TryGetParser(typeof(int?), out var number), Is.True);
        Assert.That(parser.TryGetParser(typeof(string), out var text), Is.True);
        Assert.That(number!(""), Is.Null);
        Assert.That(number("42"), Is.TypeOf<int>().And.EqualTo(42));
        Assert.That(text!(""), Is.Null);
        Assert.That(text("Ada"), Is.EqualTo("Ada"));
    }

    [Test]
    public void ParserInstances_DoNotShareCapturedCustomParsers()
    {
        var first = new SpanParser();
        var second = new SpanParser();
        var offset = 1;
        first.Register<int>(span => int.Parse(span) + offset);
        second.Register<int>(span => int.Parse(span) + 10);
        var cached = Resolve(first, false);
        offset = 2;
        Assert.That(cached("40"), Is.EqualTo(42));
        Assert.That(Resolve(second, false)("40"), Is.EqualTo(50));
    }

    private static void Register(SpanParser parser, ParseSpan<int> parse, bool field, bool runtime)
    {
        if (field)
        {
            if (runtime) parser.Register(0, typeof(int), parse);
            else parser.Register(0, parse);
        }
        else
        {
            if (runtime) parser.Register(typeof(int), parse);
            else parser.Register(parse);
        }
    }

    private static ParseSpan<object> Resolve(SpanParser parser, bool field)
    {
        ParseSpan<object>? parse;
        Assert.That(field ? parser.TryGetParser(0, out parse) : parser.TryGetParser(typeof(int), out parse), Is.True);
        return parse!;
    }
}
