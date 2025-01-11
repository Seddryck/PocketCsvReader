using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing.FieldParsing;
public class SanitizerTest
{
    private static CsvProfile GetProfile()
    {
        return new CsvProfile(new DialectDescriptorBuilder()
             .WithDelimiter(';')
             .WithLineTerminator("\r\n")
             .WithQuoteChar('`')
             .WithDoubleQuote(false)
             .WithEscapeChar('\\')
             .Build());
    }

    private static IEnumerable<ISanitizer> GetSanitizers()
    {
        var sequences = new SequenceCollection().Also(s => s.Add("NaN", null));
        var fieldEscaper = new FieldEscaper(GetProfile());

        var sanitizerFactory = new SanitizerFactory(GetProfile());
        yield return sanitizerFactory.Create(null, null);
        yield return sanitizerFactory.Create(null, fieldEscaper);
        yield return sanitizerFactory.Create(sequences, null);
        yield return sanitizerFactory.Create(sequences, fieldEscaper);
    }


    [Test]
    [TestCaseSource(nameof(GetSanitizers))]
    public void Sanitize_NotQualified_CorrectString(ISanitizer sanitizer)
    {
        var item = "foo";
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var value = sanitizer.Sanitize(buffer, false, false);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(item));
    }

    [Test]
    [TestCase("", "?")]
    [TestCase("(null)", null)]
    [TestCase("NA", "0")]
    public void Sanitize_WithSequence_Mapped(string item, string? result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var sequences = new SequenceCollection().Also(s => s.Add(item, result));
        var sanitizerFactory = new SanitizerFactory(GetProfile());
        var sanitizer = sanitizerFactory.Create(sequences, null);
        var value = sanitizer.Sanitize(buffer, false, false);

        if (result is not null)
        {
            Assert.That(value.HasValue, Is.True);
            Assert.That(value.Value.ToString(), Is.EqualTo(result));
        }
        else
            Assert.That(value.HasValue, Is.False);
    }

    [Test]
    [TestCase("", "?")]
    [TestCase("(null)", null)]
    [TestCase("NA", "0")]
    public void Sanitize_WithoutSequence_Mapped(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);


        var sanitizerFactory = new SanitizerFactory(GetProfile());
        var sanitizer = sanitizerFactory.Create(null, null);
        var value = sanitizer.Sanitize(buffer, false, false);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(item));
    }

    [Test]
    [TestCase("`(null)`", "(null)")] //Explicitly quoted (null) should be (null)
    public void Sanitize_NullButQuoted_Not(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var sequences = new SequenceCollection().Also(s => s.Add(item, null));
        var sanitizerFactory = new SanitizerFactory(GetProfile());
        var sanitizer = sanitizerFactory.Create(sequences, null);
        var value = sanitizer.Sanitize(buffer.Slice(1, item.Length - 2), false, true);

        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(result));
    }

    [TestCase("`a`", "a")]
    [TestCase("`foo`", "foo")]
    [TestCase("`foo bar`", "foo bar")]
    [TestCase("``", "?")]
    public void Sanitize_Quoted_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var sequences = new SequenceCollection().Also(s => s.Add(string.Empty, "?"));
        var sanitizerFactory = new SanitizerFactory(GetProfile());
        var sanitizer = sanitizerFactory.Create(sequences, new FieldEscaper(GetProfile()));
        var value = sanitizer.Sanitize(buffer.Slice(1, item.Length - 2), false, true);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(result));
    }

    [TestCase("`foo`", "foo")]
    [TestCase("``", "")]
    public void Sanitize_QuotedDontHandleSpecialValues_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var profile = GetProfile();
        profile.ParserOptimizations = new ParserOptimizationOptions { HandleSpecialValues = false };

        var sanitizerFactory = new SanitizerFactory(profile);
        var sanitizer = sanitizerFactory.Create(null, new FieldEscaper(profile));
        var value = sanitizer.Sanitize(buffer.Slice(1, item.Length - 2), false, true);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(result));
    }

    [Test]
    [TestCase("`a\\`b`", "a`b")]
    [TestCase("`\\`a\\`b\\``", "`a`b`")]
    public void Sanitize_EscapingQuotingChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var sanitizerFactory = new SanitizerFactory(GetProfile());
        var sanitizer = sanitizerFactory.Create(null, new FieldEscaper(GetProfile()));
        var value = sanitizer.Sanitize(buffer.Slice(1, item.Length - 2), true, true);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(result));
    }


    [Test]
    [TestCase("`a``b`", "a`b")]
    [TestCase("```a``b```", "`a`b`")]
    public void Sanitize_DoubleQuotingChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, item.Length);

        var profile = new CsvProfile(new DialectDescriptorBuilder()
            .WithQuoteChar('`')
            .WithDoubleQuote(true)
            .WithoutEscapeChar()
            .Build());

        var sanitizerFactory = new SanitizerFactory(profile);
        var sanitizer = sanitizerFactory.Create(null, new FieldEscaper(profile));
        var value = sanitizer.Sanitize(buffer.Slice(1, item.Length - 2), true, true);
        Assert.That(value.HasValue, Is.True);
        Assert.That(value.Value.ToString(), Is.EqualTo(result));
    }
}
