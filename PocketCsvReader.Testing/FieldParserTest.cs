using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Pidgin;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Testing;
public class FieldParserTest
{
    [TestCase("foo", 0, 3)]
    [TestCase("foobar", 0, 6)]
    public void Parse_Field_StartEnd(string value, int start, int length)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Delimiter = ',', QuoteChar = '\"', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));
        result = parser.ParseEof(value.Length);

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value));
    }

    [TestCase("foo;", ';', 0, 3)]
    [TestCase("foobar\t", '\t', 0, 6)]
    public void Parse_FieldDelimiter_StartEnd(string value, char sep, int start, int length)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Delimiter = sep, LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));

        Assert.That(result, Is.EqualTo(ParserState.Field));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value[..^1]));
    }

    [TestCase("foo\r\n", "\r\n", 0, 3)]
    [TestCase("foobar\0\t\0", "\0\t\0", 0, 6)]
    [TestCase("foobar|@#", "|@#", 0, 6)]
    public void Parse_FieldLineTerminator_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Delimiter = ',', LineTerminator = sep });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value[..^(sep.Length)]));
    }

    [TestCase("foobar\r", "\r", 0, 6)]
    [TestCase("foobar\n", "\n", 0, 6)]
    [TestCase("'foobar'\r", "\r", 1, 6)]
    public void Parse_FieldLineTerminatorSingleChar_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new FieldParser(
                new DialectDescriptor() { Delimiter = ',', QuoteChar = '\'', LineTerminator = sep });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo("foobar"));
    }

    [TestCase("#foo\r")]
    [TestCase("#foobar;")]
    [TestCase("#foobar")]
    public void Parse_Comment_StartEnd(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Header = false, CommentChar = '#', Delimiter = ';', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));
        Assert.That(result, Is.EqualTo(ParserState.Continue));
        result = parser.ParseEof(value.Length);
        Assert.That(result, Is.EqualTo(ParserState.Eof).Or.EqualTo(ParserState.Comment));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(0));
    }

    [TestCase("#foo\r\nbar")]
    [TestCase("#foobar;\r\nbar")]
    [TestCase("bar")]
    public void Parse_AfterComment_StartEnd(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { CommentChar = '#', Delimiter = ';', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) =>
                        {
                            var state = parser.Parse(value[i], i);
                            if (state == ParserState.Record || state == ParserState.Comment)
                                parser.Reset();
                            return state;
                        }
                        );
        result = parser.ParseEof(value.Length);

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(3));
    }


    [TestCase("foo;bar\r\n", 2)]
    [TestCase("foo;bar;brz\r\n", 3)]
    [TestCase("bar\r\n", 1)]
    public void Parse_Record_CountOfField(string value, int count)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Delimiter = ';', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Select(i => parser.Parse(value[i], i) != ParserState.Continue ? 1 : 0)
                        .Sum();

        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("foo;bar\r\nfoo;bar\r\nfoo;bar\r\n", 3)]
    [TestCase("foo;bar;brz\r\nfoo;bar;brz\r\n", 2)]
    [TestCase("bar\r\n", 1)]
    [TestCase("bar;\r\n", 1)]
    [TestCase("bar;\r\nfoo\r\n", 2)]
    public void Parse_Record_CountOfRecord(string value, int count)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Header = false, Delimiter = ';', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Select(i =>
                        {
                            var state = parser.Parse(value[i], i);
                            if (state != ParserState.Record)
                                return 0;
                            parser.Reset();
                            return 1;
                        })
                        .Sum();

        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("field_1;field_2\r\nfoo;bar\r\nfoo;bar\r\nfoo;bar\r\n", 4)]
    [TestCase("field_1\r\nbar\r\n", 2)]
    public void Parse_RecordAndHeader_CountOfRecord(string value, int count)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Header = true, Delimiter = ';', LineTerminator = "\r\n" });
        var result = Enumerable
                        .Range(0, value.Length)
                        .Select(i =>
                        {
                            var state = parser.Parse(value[i], i);
                            if (state != ParserState.Record)
                                return 0;
                            parser.Reset();
                            return 1;
                        })
                        .Sum();

        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("foo;", "foo")]
    [TestCase("'foo';", "foo")]
    [TestCase("'fo;o';", "fo;o")]
    [TestCase("'f;o;o';", "f;o;o")]
    [TestCase("';foo';", ";foo")]
    [TestCase("'f\r\noo';", "f\r\noo")]
    [TestCase("'f\ro\no';", "f\ro\no")]
    public void Parse_QuotedField_CorrectField(string value, string expected)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '\'', Delimiter = ';', DoubleQuote = false, LineTerminator = "\r\n" });
        var result = string.Empty;
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
                result = value.Substring(parser.Result.Value.Start, parser.Result.Value.Length);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("'fo''o';")]
    [TestCase("'''foo';")]
    [TestCase("'foo''';")]
    public void Parse_DoubleQuotedFieldWhenDenied_Error(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '\'', EscapeChar = '\\', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        var result = string.Empty;
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Error)
                Assert.Pass();
        Assert.Fail();
    }

    [TestCase("`fo``o`;")]
    [TestCase("```foo`;")]
    [TestCase("`foo```;")]
    public void Parse_DoubleQuotedFieldWhenAllowed_EscapedSet(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = true, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
            {
                Assert.That(parser.Result.Value.Start, Is.EqualTo(1));
                Assert.That(parser.Result.Value.Length, Is.EqualTo(5));
                Assert.That(parser.Result.Value.IsEscaped, Is.True);
            }
    }

    [TestCase(@"`fo%`o`;")]
    [TestCase(@"`%`foo`;")]
    [TestCase(@"`foo%``;")]
    public void Parse_EscapeQuoteInQuotedField_EscapedSet(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
            {
                Assert.That(parser.Result.Value.Start, Is.EqualTo(1));
                Assert.That(parser.Result.Value.Length, Is.EqualTo(5));
                Assert.That(parser.Result.Value.IsEscaped, Is.True);
            }
    }

    [TestCase(@"fo%;o;")]
    [TestCase(@"%;foo;")]
    [TestCase(@"foo%;;")]
    public void Parse_EscapeDelimiterInUnquotedField_EscapedSet(string value)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
            {
                Assert.That(parser.Result.Value.Start, Is.EqualTo(0));
                Assert.That(parser.Result.Value.Length, Is.EqualTo(5));
                Assert.That(parser.Result.Value.IsEscaped, Is.True);
            }
    }

    [TestCase("foo;bar;", 4)]
    [TestCase("foo; bar;", 5)]
    [TestCase("foo;    bar;", 8)]
    public void Parse_SkipInitialSpace_SpaceSkip(string value, int start)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field && i != value.Length - 1)
                parser.Reset();
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(3));
    }

    [TestCase("foo;`bar`;", 5)]
    [TestCase("foo; `bar`;", 6)]
    [TestCase("foo;    `bar`;", 9)]
    public void Parse_SkipInitialSpaceBeforeQuotedField_SpaceSkip(string value, int start)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field && i != value.Length - 1)
                parser.Reset();
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(3));
    }

    [TestCase("foo;`bar`;", 5)]
    [TestCase("foo;` bar`;", 5)]
    [TestCase("foo;`    bar`;", 5)]
    public void Parse_SkipInitialSpaceWithinQuotedField_SpaceNotSkip(string value, int start)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" });
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field && i != value.Length - 1)
                parser.Reset();
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(value.Length - 7));
    }


    [TestCase("[foo,bar];", '[', ']', ',')]
    [TestCase("<foo\tbar>;", '<', '>', '\t')]
    [TestCase("{foo|bar};", '{', '}', '|')]
    [TestCase("(foo^bar);", '(', ')', '^')]
    public void Parse_Array_PrefixSuffixSkipped(string value, char prefix, char suffix, char delimiter)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Header = false, Delimiter = ';', ArrayDelimiter = delimiter, ArrayPrefix = prefix, ArraySuffix = suffix });
        var result = string.Empty;
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
                result = value.Substring(parser.Result.Value.Start, parser.Result.Value.Length);
        Assert.That(result, Does.StartWith("foo"));
        Assert.That(result, Does.EndWith("bar"));
    }

    [TestCase("foo,bar;", ',')]
    public void Parse_Array_WithoutPrefixSuffix(string value, char delimiter)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { Header = false, Delimiter = ';', ArrayDelimiter = delimiter });
        var result = string.Empty;
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
                result = value.Substring(parser.Result.Value.Start, parser.Result.Value.Length);
        Assert.That(result, Does.StartWith("foo"));
        Assert.That(result, Does.EndWith("bar"));
    }

    [TestCase("'foo';", "foo")]
    [TestCase("[foo];", "foo")]
    public void Parse_SurrounderChars_CorrectField(string value, string expected)
    {
        var parser = new FieldParser(
            new DialectDescriptor() { QuoteChar = '\'', Delimiter = ';', ArrayDelimiter = ',', ArrayPrefix = '[', ArraySuffix = ']' });
        var result = string.Empty;
        for (int i = 0; i < value.Length; i++)
            if (parser.Parse(value[i], i) == ParserState.Field)
                result = value.Substring(parser.Result.Value.Start, parser.Result.Value.Length);
        Assert.That(result, Is.EqualTo("foo"));
    }
}
