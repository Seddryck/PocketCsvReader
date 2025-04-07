using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Configuration;
public class DialectDescriptorBuilderTest
{
    [Test]
    [TestCase(';')]
    [TestCase(',')]
    [TestCase("\t")]
    [TestCase("|")]
    public void WithDelimiter_ShouldSetDelimiter(char delimiter)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .Build();
        Assert.That(descriptor.Delimiter, Is.EqualTo(delimiter));
    }

    [Test]
    [TestCase(Delimiter.Comma, ',')]
    [TestCase(Delimiter.Semicolon, ';')]
    [TestCase(Delimiter.Tab, '\t')]
    [TestCase(Delimiter.Pipe, '|')]
    public void WithDelimiter_ShouldSetDelimiter(Delimiter delimiter, char value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .Build();
        Assert.That(descriptor.Delimiter, Is.EqualTo(value));
    }

    [Test]
    [TestCase("\r\n")]
    [TestCase("\n")]
    [TestCase("\r")]
    public void WithLineTerminator_ShouldSetLineTerminator(string lineTerminator)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithLineTerminator(lineTerminator)
            .Build();

        Assert.That(descriptor.LineTerminator, Is.EqualTo(lineTerminator));
    }

    [Test]
    [TestCase(LineTerminator.CarriageReturnLineFeed, "\r\n")]
    [TestCase(LineTerminator.CarriageReturn, "\r")]
    [TestCase(LineTerminator.LineFeed, "\n")]
    public void WithLineTerminator_ShouldSetLineTerminator(LineTerminator lineTerminator, string value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithLineTerminator(lineTerminator)
            .Build();
        Assert.That(descriptor.LineTerminator, Is.EqualTo(value));
    }

    [Test]
    [TestCase('"')]
    [TestCase('\'')]
    public void WithQuoteChar_ShouldSetQuoteChar(char quoteChar)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithQuoteChar(quoteChar)
            .Build();

        Assert.That(descriptor.QuoteChar, Is.EqualTo(quoteChar));
    }

    [Test]
    [TestCase(QuoteChar.SingleQuote, '\'')]
    [TestCase(QuoteChar.DoubleQuote, '\"')]
    public void WithQuoteChar_ShouldSetQuoteChar(QuoteChar quoteChar, char value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithQuoteChar(quoteChar)
            .Build();
        Assert.That(descriptor.QuoteChar, Is.EqualTo(value));
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void WithDoubleQuote_ShouldSetDoubleQuoteToValue(bool value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithDoubleQuote(value)
            .Build();

        Assert.That(descriptor.DoubleQuote, Is.EqualTo(value));
    }

    [Test]
    public void WithDoubleQuote_ShouldSetDoubleQuoteToTrue()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithDoubleQuote()
            .Build();

        Assert.That(descriptor.DoubleQuote, Is.True);
    }

    [Test]
    public void WithoutDoubleQuote_ShouldSetDoubleQuoteToFalse()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutDoubleQuote()
            .Build();

        Assert.That(descriptor.DoubleQuote, Is.False);
    }

    [Test]
    public void WithEscapeChar_ShouldSetEscapeChar()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithEscapeChar('\\')
            .Build();

        Assert.That(descriptor.EscapeChar, Is.EqualTo('\\'));
    }

    [Test]
    [TestCase(EscapeChar.BackSlash, '\\')]
    [TestCase(EscapeChar.ForwardSlash, '/')]
    public void WithEscapeChar_ShouldSetEscapeChar(EscapeChar escapeChar, char value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithEscapeChar(escapeChar)
            .Build();
        Assert.That(descriptor.EscapeChar, Is.EqualTo(value));
    }

    [Test]
    public void WithNullSequence_ShouldSetNullSequence()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithNullSequence("(null)")
            .Build();

        Assert.That(descriptor.NullSequence, Is.EqualTo("(null)"));
    }

    [Test]
    public void WithoutNullSequence_ShouldSetNullSequenceToNull()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutNullSequence()
            .Build();

        Assert.That(descriptor.NullSequence, Is.Null);
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void WithSkipInitialSpace_ShouldSetSkipInitialSpaceToTrue(bool value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithSkipInitialSpace(value)
            .Build();

        Assert.That(descriptor.SkipInitialSpace, Is.EqualTo(value));
    }

    [Test]
    public void WithoutSkipInitialSpace_ShouldSetSkipInitialSpaceToFalse()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutSkipInitialSpace()
            .Build();

        Assert.That(descriptor.SkipInitialSpace, Is.False);
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void WithHeader_ShouldSetHeaderToValue(bool value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithHeader(value)
            .Build();

        Assert.That(descriptor.Header, Is.EqualTo(value));
    }

    [Test]
    public void WithHeader_ShouldSetHeaderToTrue()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithHeader()
            .Build();

        Assert.That(descriptor.Header, Is.True);
        Assert.That(descriptor.HeaderRows, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void SwitchHeaderValue_ShouldSetHeaderToTrue()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutHeader()
            .WithHeader()
            .Build();

        Assert.That(descriptor.Header, Is.True);
        Assert.That(descriptor.HeaderRows, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void WithoutHeader_ShouldSetHeaderToFalse()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutHeader()
            .Build();

        Assert.That(descriptor.Header, Is.False);
        Assert.That(descriptor.HeaderRows, Is.Empty);
    }

    [Test]
    [TestCase(" ")]
    [TestCase("-")]
    [TestCase(" - ")]
    public void WithHeaderJoin_ShouldSetHeaderJoin(string join)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithHeaderJoin(join)
            .Build();

        Assert.That(descriptor.HeaderJoin, Is.EqualTo(join));
    }

    [Test]
    [TestCase(1)]
    [TestCase(1, 2, 3)]
    public void WithHeaderRows_ShouldSetHeaderRows(params int[] rows)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithHeaderRows(rows)
            .Build();

        Assert.That(descriptor.HeaderRows, Is.EqualTo(rows));
    }

    [Test]
    public void WithHeaderRowsEmpty_ShouldSetHeaderRowsAndHeader()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithHeaderRows([])
            .Build();

        Assert.That(descriptor.HeaderRows, Is.Empty);
        Assert.That(descriptor.Header, Is.False);
    }

    [Test]
    public void WithoutHeaderRows_ShouldSetHeaderRowsAndHeader()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithoutHeaderRows()
            .Build();

        Assert.That(descriptor.HeaderRows, Is.Empty);
        Assert.That(descriptor.Header, Is.False);
    }

    [Test]
    [TestCase("#")]
    [TestCase("/")]
    public void WithCommentChar_ShouldSetCommentChar(char commentChar)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithCommentChar(commentChar)
            .Build();

        Assert.That(descriptor.CommentChar, Is.EqualTo(commentChar));
    }

    [Test]
    [TestCase(CommentChar.Hash, '#')]
    [TestCase(CommentChar.ForwardSlash, '/')]
    [TestCase(CommentChar.Dash, '-')]
    [TestCase(CommentChar.Semicolon, ';')]
    public void WithCommentChar_ShouldSetCommentChar(CommentChar commentChar, char value)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithCommentChar(commentChar)
            .Build();
        Assert.That(descriptor.CommentChar, Is.EqualTo(value));
    }

    [Test]
    [TestCase()]
    [TestCase(1)]
    [TestCase(1, 2, 3)]
    public void WithCommentRows_ShouldSetCommentChar(params int[] rows)
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithCommentRows(rows)
            .Build();

        Assert.That(descriptor.CommentRows, Is.EqualTo(rows));
    }

    [Test]
    public void WithCsvDdfVersion_ShouldSetCsvDdfVersionToValue()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithDelimiter('\t')
            .WithLineTerminator("\r")
            .WithQuoteChar('\'')
            .WithDoubleQuote()
            .Build();
        var csvReader = new CsvReader(new CsvProfile(descriptor));

        Assert.That(csvReader.Profile.Dialect.Delimiter, Is.EqualTo('\t'));
        Assert.That(csvReader.Profile.Dialect.LineTerminator, Is.EqualTo("\r"));
        Assert.That(csvReader.Profile.Dialect.QuoteChar, Is.EqualTo('\''));
        Assert.That(csvReader.Profile.Dialect.DoubleQuote, Is.True);
    }

    [Test]
    public void WithArrayDelimiter_ShouldSetArrayDelimiter()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(','))
            .Build();
        var csvReader = new CsvReader(new CsvProfile(descriptor));

        Assert.That(csvReader.Profile.Dialect.ArrayDelimiter, Is.EqualTo(','));
    }

    [Test]
    public void WithArrayDelimiterEnum_ShouldSetArrayDelimiter()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(Delimiter.Comma))
            .Build();
        var csvReader = new CsvReader(new CsvProfile(descriptor));

        Assert.That(csvReader.Profile.Dialect.ArrayDelimiter, Is.EqualTo(','));
    }

    [Test]
    public void WithArraySuffixPrefix_ShouldSetArraySuffixPrefix()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(',').WithPrefix('[').WithSuffix(']'))
            .Build();
        var csvReader = new CsvReader(new CsvProfile(descriptor));

        Assert.That(csvReader.Profile.Dialect.ArrayPrefix, Is.EqualTo('['));
        Assert.That(csvReader.Profile.Dialect.ArraySuffix, Is.EqualTo(']'));
    }
    [Test]
    public void WithoutPrefixAndSuffix_ShouldThrows()
    {
        var descriptor = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(',').WithPrefix('[')
                .WithSuffix(']').WithoutPrefixAndSuffix())
            .Build();
        var csvReader = new CsvReader(new CsvProfile(descriptor));
        Assert.That(csvReader.Profile.Dialect.ArrayPrefix, Is.Null);
        Assert.That(csvReader.Profile.Dialect.ArraySuffix, Is.Null);
    }

    [Test]
    public void WithoutArrayPrefix_ShouldThrows()
    {
        var builder = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(',').WithSuffix(']'));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void WithoutArraySuffix_ShouldThrows()
    {
        var builder = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithDelimiter(',').WithPrefix('['));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void WithoutArrayDelimiter_ShouldThrows()
    {
        var builder = new DialectDescriptorBuilder()
            .WithArray(builder => builder.WithPrefix('[').WithSuffix(']'));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
