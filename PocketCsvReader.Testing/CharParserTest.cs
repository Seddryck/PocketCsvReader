using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class CharParserTest
{
    [TestCase("foo", 0, 3)]
    [TestCase("foobar", 0, 6)]
    public void Parse_Field_StartEnd(string value, int start, int length)
    {
        var parser = new CharParser(CsvProfile.CommaDoubleQuote);
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));
        result = parser.ParseEof();

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value));
    }

    [TestCase("foo;", ';', 0, 3)]
    [TestCase("foobar\t", '\t', 0, 6)]
    public void Parse_FieldDelimiter_StartEnd(string value, char sep, int start, int length)
    {
        var parser = new CharParser(new CsvProfile(sep, "\r\n"));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));

        Assert.That(result, Is.EqualTo(ParserState.Field));
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value[..^1]));
    }


    [TestCase("foo\r\n", "\r\n", 0, 3)]
    [TestCase("foobar\0\t\0", "\0\t\0", 0, 6)]
    [TestCase("foobar|@#", "|@#", 0, 6)]
    public void Parse_FieldLineTerminator_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new CharParser(new CsvProfile(',', sep));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo(value[..^(sep.Length)]));
    }

    [TestCase("foobar\r", "\r", 0, 6)]
    [TestCase("foobar\n", "\n", 0, 6)]
    [TestCase("'foobar'\r", "\r", 1, 6)]
    public void Parse_FieldLineTerminatorSingleChar_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new CharParser(new CsvProfile(
                new DialectDescriptor() { Delimiter = ',', QuoteChar = '\'', LineTerminator = sep }));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(length));
        Assert.That(value.Substring(start, length), Is.EqualTo("foobar"));
    }

    [TestCase("#foo\r")]
    [TestCase("#foobar;")]
    [TestCase("#foobar")]
    public void Parse_Comment_StartEnd(string value)
    {
        var parser = new CharParser(new CsvProfile(new DialectDescriptor() { CommentChar = '#', Delimiter = ';', LineTerminator = "\r\n" }));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));

        Assert.That(result, Is.EqualTo(ParserState.Continue));
        Assert.That(parser.FieldLength, Is.EqualTo(0));
    }

    [TestCase("#foo\r\nbar")]
    [TestCase("#foobar;\r\nbar")]
    [TestCase("bar")]
    public void Parse_AfterComment_StartEnd(string value)
    {
        var parser = new CharParser(new CsvProfile(new DialectDescriptor() { CommentChar = '#', Delimiter = ';', LineTerminator = "\r\n" }));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));
        result = parser.ParseEof();

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.FieldLength, Is.EqualTo(3));
    }


    [TestCase("foo;bar\r\n", 2)]
    [TestCase("foo;bar;brz\r\n", 3)]
    [TestCase("bar\r\n", 1)]
    public void Parse_Record_CountOfField(string value, int count)
    {
        var parser = new CharParser(new CsvProfile(new DialectDescriptor() { Delimiter = ';', LineTerminator = "\r\n" }));
        var result = value.Aggregate(0, (current, c)
            => parser.Parse(c) != ParserState.Continue ? current + 1 : current);

        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("foo;bar\r\nfoo;bar\r\nfoo;bar\r\n", 3)]
    [TestCase("foo;bar;brz\r\nfoo;bar;brz\r\n", 2)]
    [TestCase("bar\r\n", 1)]
    [TestCase("bar;\r\n", 1)]
    [TestCase("bar;\r\nfoo\r\n", 2)]
    public void Parse_Record_CountOfRecord(string value, int count)
    {
        var parser = new CharParser(new CsvProfile(new DialectDescriptor() { Delimiter = ';', LineTerminator = "\r\n" }));
        var result = value.Aggregate(0, (current, c)
            => parser.Parse(c) == ParserState.Record ? current + 1 : current);

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
        var parser = new CharParser(new CsvProfile(new DialectDescriptor() { QuoteChar = '\'', Delimiter = ';', LineTerminator = "\r\n" }));
        var result = string.Empty;
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Field)
                result = value.Substring(parser.FieldStart, parser.FieldLength);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("'fo''o';")]
    [TestCase("'''foo';")]
    [TestCase("'foo''';")]
    public void Parse_DoubleQuotedFieldWhenDenied_Error(string value)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { QuoteChar = '\'', EscapeChar = '\\', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        var result = string.Empty;
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Error)
                Assert.Pass();
        Assert.Fail();
    }

    [TestCase("`fo``o`;")]
    [TestCase("```foo`;")]
    [TestCase("`foo```;")]
    public void Parse_DoubleQuotedFieldWhenAllowed_EscapedSet(string value)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = true, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Field)
            {
                Assert.That(parser.FieldStart, Is.EqualTo(1));
                Assert.That(parser.FieldLength, Is.EqualTo(5));
                Assert.That(parser.IsEscapedField, Is.True);
            }
    }

    [TestCase(@"`fo%`o`;")]
    [TestCase(@"`%`foo`;")]
    [TestCase(@"`foo%``;")]
    public void Parse_EscapeQuoteInQuotedField_EscapedSet(string value)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Field)
            {
                Assert.That(parser.FieldStart, Is.EqualTo(1));
                Assert.That(parser.FieldLength, Is.EqualTo(5));
                Assert.That(parser.IsEscapedField, Is.True);
            }
    }

    [TestCase(@"fo%;o;")]
    [TestCase(@"%;foo;")]
    [TestCase(@"foo%;;")]
    public void Parse_EscapeDelimiterInUnquotedField_EscapedSet(string value)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Field)
            {
                Assert.That(parser.FieldStart, Is.EqualTo(0));
                Assert.That(parser.FieldLength, Is.EqualTo(5));
                Assert.That(parser.IsEscapedField, Is.True);
            }
    }

    [TestCase("foo;bar;", 4)]
    [TestCase("foo; bar;", 5)]
    [TestCase("foo;    bar;", 8)]
    public void Parse_SkipInitialSpace_SpaceSkip(string value, int start)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            parser.Parse(c);
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(3));
    }

    [TestCase("foo;`bar`;", 5)]
    [TestCase("foo; `bar`;", 6)]
    [TestCase("foo;    `bar`;", 9)]
    public void Parse_SkipInitialSpaceBeforeQuotedField_SpaceSkip(string value, int start)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            parser.Parse(c);
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(3));
    }

    [TestCase("foo;`bar`;", 5)]
    [TestCase("foo;` bar`;", 5)]
    [TestCase("foo;`    bar`;", 5)]
    public void Parse_SkipInitialSpaceWithinQuotedField_SpaceNotSkip(string value, int start)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { SkipInitialSpace = true, QuoteChar = '`', EscapeChar = '%', DoubleQuote = false, Delimiter = ';', LineTerminator = "\r\n" }));
        foreach (var c in value)
            parser.Parse(c);
        Assert.That(parser.FieldStart, Is.EqualTo(start));
        Assert.That(parser.FieldLength, Is.EqualTo(value.Length - 7));
    }

    [TestCase("foo\r\nbar\r\n")]
    [TestCase("Comment\r\nfoo\r\nbar\r\n", 1)]
    [TestCase("Comment 1\r\nComment 2\r\nfoo\r\nbar\r\n", 1, 2)]
    [TestCase("Comment 1\r\nComment 2\r\nfoo\r\nbar\r\nComment 3", 1, 2, 5)]
    [TestCase("Comment 1\r\n\r\nfooComment 2\r\nbar\r\nComment 3", 1, 3, 5)]
    public void Parse_CommentRows_CommentsSkipped(string value, params int[] commentRows)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { CommentRows = commentRows, LineTerminator = "\r\n" }));
        var recordCount = 0;
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Record)
                recordCount++;
        Assert.That(recordCount, Is.EqualTo(2));
    }


    [TestCase("foo\r\nbar\r\n")]
    [TestCase("Comment\r\nfoo\r\nbar\r\n#Comment", 1)]
    [TestCase("Comment 1\r\nComment 2\r\nfoo\r\n#Comment\r\nbar\r\n#Comment", 1, 2)]
    [TestCase("Comment 1\r\nComment 2\r\nfoo\r\n\r\n#Commentbar\r\nComment 3", 1, 2, 6)]
    [TestCase("Comment 1\r\n\r\nfooComment 2\r\nbar\r\n#Comment\r\nComment 3", 1, 3, 6)]
    public void Parse_CommentRowsAndComments_CommentsSkipped(string value, params int[] commentRows)
    {
        var parser = new CharParser(new CsvProfile(
            new DialectDescriptor() { CommentChar='#', CommentRows = commentRows, LineTerminator = "\r\n" }));
        var recordCount = 0;
        foreach (var c in value)
            if (parser.Parse(c) == ParserState.Record)
                recordCount++;
        Assert.That(recordCount, Is.EqualTo(2));
    }
}
