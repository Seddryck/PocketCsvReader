using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.CharParsing;
public class FieldParserTest
{
    [Test]
    [TestCase("foo;", ';')]
    [TestCase("foobar,", ',')]
    public void Parse_RawField_Parsed(string buffer, char delimiter)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .Build());

        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        Assert.That(parser.Context.Span.Value.IsComplete, Is.True);
        Assert.That(parser.Context.Span.Value.Start, Is.EqualTo(0));
        Assert.That(parser.Context.Span.Value.Length, Is.EqualTo(buffer.Length - 1));
        Assert.That(parser.Context.Span.Value.WasQuoted, Is.False);
        Assert.That(parser.Context.Span.Value.IsEscaped, Is.False);
    }

    [Test]
    [TestCase("'foo';", ';', '\'')]
    [TestCase("\"foobar\",", ',', '\"')]
    public void Parse_QuotedField_Parsed(string buffer, char delimiter, char quote)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .WithQuoteChar(quote)
            .WithDoubleQuote(false)
            .Build());

        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        Assert.That(parser.Context.Span.Value.IsComplete, Is.True);
        Assert.That(parser.Context.Span.Value.Start, Is.EqualTo(1));
        Assert.That(parser.Context.Span.Value.Length, Is.EqualTo(buffer.Length - 3));
        Assert.That(parser.Context.Span.Value.WasQuoted, Is.True);
        Assert.That(parser.Context.Span.Value.IsEscaped, Is.False);
    }

    [Test]
    [TestCase("'f''o''''o''';", ';', '\'')]
    [TestCase("\"foo\"\"bar\",", ',', '\"')]
    public void Parse_DoubleQuotedField_Parsed(string buffer, char delimiter, char quote)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .WithQuoteChar(quote)
            .WithDoubleQuote(true)
            .Build());

        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        Assert.That(parser.Context.Span.Value.IsComplete, Is.True);
        Assert.That(parser.Context.Span.Value.Start, Is.EqualTo(1));
        Assert.That(parser.Context.Span.Value.Length, Is.EqualTo(buffer.Length - 3));
        Assert.That(parser.Context.Span.Value.WasQuoted, Is.True);
        Assert.That(parser.Context.Span.Value.IsEscaped, Is.True);
    }

    [Test]
    [TestCase("fo\\;o;", ';', '\'', '\\', true)]
    [TestCase("foo`,bar,", ',', '\"', '`', true)]
    [TestCase("foo;", ';', '\'', '\\', false)]
    [TestCase("foobar,", ',', '\"', '`', false)]
    [TestCase("'fo\\;o';", ';', '\'', '\\', true)]
    [TestCase("\"foo`\"bar\",", ',', '\"', '`', true)]
    public void Parse_EscapedField_Parsed(string buffer, char delimiter, char quote, char escape, bool expected)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(delimiter)
            .WithQuoteChar(quote)
            .WithEscapeChar(escape)
            .WithDoubleQuote(false)
            .Build());

        for (int i = 0; i < buffer.Length; i++)
            parser.Parse(buffer[i], i);

        Assert.That(parser.Context.Span.Value.IsComplete, Is.True);
        Assert.That(parser.Context.Span.Value.IsEscaped, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("foo;", 1)]
    [TestCase("'foo';", 1)]
    [TestCase("'fo''o';", 1)]
    [TestCase("'foo;bar';", 1)]
    [TestCase("foo\r\n", 1)]
    [TestCase("'foo';\r\n", 2)]
    [TestCase("foo;bar;", 2)]
    [TestCase("foo;bar\r\n", 2)]
    [TestCase("foo;bar;\r\n", 3)]
    [TestCase("foo;bar;qrz;", 3)]
    [TestCase("foo;bar;qrz\r\n", 3)]
    [TestCase("foo;bar;qrz;\r\n", 4)]
    [TestCase(";", 1)]
    [TestCase("\r\n", 1)]
    public void Parse_ConsecutiveFields_Parsed(string buffer, int expected)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithQuoteChar('\'')
            .WithLineTerminator("\r\n")
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
            {
                completed++;
                parser.Reset();
            }
        }
        Assert.That(completed, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("foo\rbar\r\n")]
    public void Parse_IncompleteLineTerminator_Parsed(string buffer)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithLineTerminator("\r\n")
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
                completed++;
        }

        Assert.That(parser.Context.Span.Value.Start, Is.EqualTo(0));
        Assert.That(parser.Context.Span.Value.Length, Is.EqualTo(7));
        Assert.That(completed, Is.EqualTo(1));
    }


    [Test]
    [TestCase("foobar\r", "\r")]
    [TestCase("f\\\rbar\r", "\r")]
    [TestCase("foobar\t\r", "\t\r")]
    [TestCase("fo\tbar\t\r", "\t\r")]
    [TestCase("f\\\t\rar\t\r", "\t\r")]
    [TestCase("f\t\\\rar\t\r", "\t\r")]
    [TestCase("foobar\t\r\n", "\t\r\n")]
    public void Parse_CustomLineTerminator_Parsed(string buffer, string lineTerminator)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithEscapeChar('\\')
            .WithLineTerminator(lineTerminator)
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
                completed++;
        }

        Assert.That(parser.Context.Span.Value.Start, Is.EqualTo(0));
        Assert.That(parser.Context.Span.Value.Length, Is.EqualTo(6));
        Assert.That(completed, Is.EqualTo(1));
    }

    [Test]
    [TestCase("[foo|bar]\r\n", 2)]
    [TestCase("[foo|bar|qrz];", 3)]
    public void Parse_Array_Parsed(string buffer, int expected)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithLineTerminator("\r\n")
            .WithArray(array => array
                .WithPrefix('[')
                .WithDelimiter('|')
                .WithSuffix(']'))
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
                completed++;
        }

        Assert.That(parser.Context.Span.Children!.Length, Is.EqualTo(expected));
        Assert.That(parser.Context.Span.Children[0].Value.Start, Is.EqualTo(1));
        Assert.That(parser.Context.Span.Children[0].Value.Length, Is.EqualTo(3));
        Assert.That(parser.Context.Span.Children[1].Value.Start, Is.EqualTo(5));
        Assert.That(parser.Context.Span.Children[1].Value.Length, Is.EqualTo(3));
        if (expected > 2)
        {
            Assert.That(parser.Context.Span.Children[2].Value.Start, Is.EqualTo(9));
            Assert.That(parser.Context.Span.Children[2].Value.Length, Is.EqualTo(3));
        }
        Assert.That(completed, Is.EqualTo(1));
    }

    [Test]
    [TestCase("['fo''o'|bar];", 2)]
    [TestCase(@"[f\'oo|'bar'|qrz];", 3)]
    public void Parse_ArrayQuotedEscaped_Parsed(string buffer, int expected)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithQuoteChar('\'')
            .WithEscapeChar('\\')
            .WithDoubleQuote(true)
            .WithLineTerminator("\r\n")
            .WithArray(array => array
                .WithPrefix('[')
                .WithDelimiter('|')
                .WithSuffix(']'))
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
                completed++;
        }

        Assert.That(parser.Context.Span.Children!.Length, Is.EqualTo(expected));
        Assert.That(completed, Is.EqualTo(1));
    }

    [Test]
    [TestCase("[foo|bar];[1|2|3]\r\n")]
    [TestCase("[foo|bar]\r\n[1|2|3]\r\n")]
    public void Parse_TwoArrays_Parsed(string buffer)
    {
        var parser = new FieldParser(new DialectDescriptorBuilder()
            .WithDelimiter(';')
            .WithLineTerminator("\r\n")
            .WithArray(array => array
                .WithPrefix('[')
                .WithDelimiter('|')
                .WithSuffix(']'))
            .Build());

        var completed = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var result = parser.Parse(buffer[i], i);
            if (result == ParserState.Error)
                Assert.Fail($"Unexpected error at position {i}");
            if (result == ParserState.Field || result == ParserState.Record)
            {
                completed++;
                if (completed==1)
                {
                    Assert.That(parser.Context.Span.Children!.Length, Is.EqualTo(2));
                    Assert.That(parser.Context.Span.Children[0].Value.Start, Is.EqualTo(1));
                    Assert.That(parser.Context.Span.Children[0].Value.Length, Is.EqualTo(3));
                    Assert.That(parser.Context.Span.Children[1].Value.Start, Is.EqualTo(5));
                    Assert.That(parser.Context.Span.Children[1].Value.Length, Is.EqualTo(3));
                }
                else
                {
                    Assert.That(parser.Context.Span.Children!.Length, Is.EqualTo(3));
                    Assert.That(parser.Context.Span.Children[0].Value.Start, Is.EqualTo(buffer.Length - 8));
                    Assert.That(parser.Context.Span.Children[0].Value.Length, Is.EqualTo(1));
                    Assert.That(parser.Context.Span.Children[1].Value.Start, Is.EqualTo(buffer.Length - 6));
                    Assert.That(parser.Context.Span.Children[1].Value.Length, Is.EqualTo(1));
                    Assert.That(parser.Context.Span.Children[2].Value.Start, Is.EqualTo(buffer.Length - 4));
                    Assert.That(parser.Context.Span.Children[2].Value.Length, Is.EqualTo(1));
                }
                parser.Reset();
            }
        }
        Assert.That(completed, Is.EqualTo(2));
    }
}
