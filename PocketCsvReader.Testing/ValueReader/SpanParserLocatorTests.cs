using System.Globalization;
using NUnit.Framework;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing.ValueReader;

public class SpanParserLocatorTests
{
    public readonly record struct Both(int Value)
    {
        public static Both ParseExact(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, IFormatProvider culture, DateTimeStyles styles)
        {
            Assert.That(pattern.SequenceEqual("number"), Is.True);
            Assert.That(culture, Is.SameAs(CultureInfo.InvariantCulture));
            Assert.That(styles, Is.EqualTo(DateTimeStyles.None));
            return new(int.Parse(text, culture));
        }

        public static Both ParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles)
            => throw new AssertionException("String ParseExact must not be used when a matching span overload exists.");

        public static bool TryParseExact(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, IFormatProvider culture, DateTimeStyles styles, out Both value)
        {
            Assert.That(pattern.SequenceEqual("number"), Is.True);
            Assert.That(culture, Is.SameAs(CultureInfo.InvariantCulture));
            Assert.That(styles, Is.EqualTo(DateTimeStyles.None));
            var success = int.TryParse(text, NumberStyles.Integer, culture, out var number);
            value = new(number);
            return success;
        }

        public static bool TryParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles, out Both value)
            => throw new AssertionException("String TryParseExact must not be used when a matching span overload exists.");
    }

    [Test]
    public void BothOverloads_PreferSpanForParseAndTryParseIncludingPattern()
    {
        var locator = new TypeParserLocator<Both>();
        object[] options = ["number", CultureInfo.InvariantCulture, DateTimeStyles.None];
        var parse = locator.LocateSpanParser(options);
        var safe = (TryParseSpan<Both>)locator.LocateTryParser(options)!;
        Assert.That(parse("42"), Is.EqualTo(new Both(42)));
        Assert.That(safe("43", out var value), Is.True);
        Assert.That(value, Is.EqualTo(new Both(43)));
        Assert.That(safe("invalid", out _), Is.False);
        Assert.That(locator.Locate(options)("44"), Is.EqualTo(new Both(44)));
    }

    public readonly record struct SpanOnly(int Value)
    {
        public static SpanOnly Parse(ReadOnlySpan<char> text) => new(int.Parse(text));
        public static bool TryParse(ReadOnlySpan<char> text, out SpanOnly value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    [Test]
    public void SpanOnlyType_RequiresNoStringOverload()
    {
        ITypeParserLocator locator = new TypeParserLocator<SpanOnly>();
        var parse = (ParseSpan<SpanOnly>)locator.LocateSpanParser([]);
        var safe = (TryParseSpan<SpanOnly>)locator.LocateTryParser([])!;
        Assert.That(parse("42"), Is.EqualTo(new SpanOnly(42)));
        Assert.That(safe("42", out var value), Is.True);
        Assert.That(value, Is.EqualTo(new SpanOnly(42)));
        Assert.That(safe("invalid", out _), Is.False);
    }

    public readonly record struct StringOnly(int Value)
    {
        public static StringOnly Parse(string text) => new(int.Parse(text));
        public static bool TryParse(string text, out StringOnly value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    [Test]
    public void StringOnlyType_AdaptsBothOperationsToSpanDelegates()
    {
        var locator = new TypeParserLocator<StringOnly>();
        var parse = locator.LocateSpanParser([]);
        var safe = (TryParseSpan<StringOnly>)locator.LocateTryParser([])!;
        Assert.That(parse("42"), Is.EqualTo(new StringOnly(42)));
        Assert.That(safe("43", out var value), Is.True);
        Assert.That(value, Is.EqualTo(new StringOnly(43)));
        Assert.That(safe("invalid", out _), Is.False);
        Assert.That(() => parse("invalid"), Throws.TypeOf<FormatException>());
    }

    public readonly record struct StringParseSpanTry(int Value)
    {
        public static StringParseSpanTry Parse(string text) => new(int.Parse(text));
        public static bool TryParse(ReadOnlySpan<char> text, out StringParseSpanTry value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    public readonly record struct SpanParseStringTry(int Value)
    {
        public static SpanParseStringTry Parse(ReadOnlySpan<char> text) => new(int.Parse(text));
        public static bool TryParse(string text, out SpanParseStringTry value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    [Test]
    public void MixedInputOverloads_ResolveEachOperationIndependently()
    {
        var first = new TypeParserLocator<StringParseSpanTry>();
        Assert.That(first.LocateSpanParser([])("42"), Is.EqualTo(new StringParseSpanTry(42)));
        var firstTry = (TryParseSpan<StringParseSpanTry>)first.LocateTryParser([])!;
        Assert.That(firstTry("43", out var firstValue), Is.True);
        Assert.That(firstValue, Is.EqualTo(new StringParseSpanTry(43)));
        Assert.That(firstTry("invalid", out _), Is.False);
        var second = new TypeParserLocator<SpanParseStringTry>();
        Assert.That(second.LocateSpanParser([])("42"), Is.EqualTo(new SpanParseStringTry(42)));
        var secondTry = (TryParseSpan<SpanParseStringTry>)second.LocateTryParser([])!;
        Assert.That(secondTry("43", out var secondValue), Is.True);
        Assert.That(secondValue, Is.EqualTo(new SpanParseStringTry(43)));
        Assert.That(secondTry("invalid", out _), Is.False);
    }

    public readonly record struct ConfiguredString(int Value)
    {
        public static ConfiguredString Parse(ReadOnlySpan<char> text)
            => throw new AssertionException("Span preference must not discard configured format and culture.");
        public static ConfiguredString ParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles)
        {
            Assert.That(pattern, Is.EqualTo("number"));
            return new(int.Parse(text, culture));
        }
    }

    [Test]
    public void ConfiguredStringOverload_TakesPrecedenceOverLessSpecificSpanOverload()
    {
        var locator = new TypeParserLocator<ConfiguredString>();
        object[] options = ["number", CultureInfo.InvariantCulture, DateTimeStyles.None];
        Assert.That(locator.LocateSpanParser(options)("42"), Is.EqualTo(new ConfiguredString(42)));
        Assert.That(locator.LocateTryParser(options), Is.Null);
    }
}
