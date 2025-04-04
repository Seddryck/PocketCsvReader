using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;
public class CharParserTest
{
    [TestCase("{\"foo\": \"bar\"}", 2, 3, 9, 3)]
    [TestCase("{\"foo\": 123}", 2, 3, 8, 3)]
    public void Parse_Field_StartEnd(string value, int startLabel, int lengthLabel, int startValue, int lengthValue)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));
        result = parser.ParseEof();

        Assert.That(result, Is.EqualTo(ParserState.Record));
        Assert.That(parser.LabelStart, Is.EqualTo(startLabel));
        Assert.That(parser.LabelLength, Is.EqualTo(lengthLabel));
        Assert.That(parser.ValueStart, Is.EqualTo(startValue));
        Assert.That(parser.ValueLength, Is.EqualTo(lengthValue));
    }

    [TestCase("{\"foo\": \"bar\"}\r\n", "\r\n", 9, 3)]
    [TestCase("{\"foo\": \"bar\"}\n", "\n", 9, 3)]
    public void Parse_FieldLineTerminator_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new CharParser(new NdjsonProfile(sep));
        var result = value.Aggregate((ParserState?)null, (current, c) => parser.Parse(c));

        Assert.That(result, Is.EqualTo(ParserState.Continue));
        Assert.That(parser.ValueStart, Is.EqualTo(start));
        Assert.That(parser.ValueLength, Is.EqualTo(length));
    }

    [TestCase("{\"foo\": \"bar\"}", 1)]
    [TestCase("{\"foo\": 123, \"bar\": true}", 2)]
    [TestCase("{\"foo\": \"value\", \"bar\": true, \"qrz\": 123}", 3)]
    public void Parse_Record_CountOfField(string value, int count)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var result = value.Aggregate(0, (current, c)
            => parser.Parse(c) != ParserState.Continue ? current + 1 : current);
        
        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("{\"foo\": \"bar\"}\r\n{\"foo\": \"qrz\"}", 2)]
    [TestCase("{\"foo\": \"bar\"}\r\n{\"foo\": \"qrz\"}\r\n", 2)]
    public void Parse_Record_CountOfRecord(string value, int count)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var result = value.Aggregate(0, (current, c)
            => parser.Parse(c) == ParserState.Record ? current + 1 : current);
        Assert.That(result, Is.EqualTo(count));
    }

    [TestCase("{\"foo\": \"bar\"}", "bar")]
    [TestCase("{\"foo\": 123}", "123")]
    [TestCase("{\"foo\": true}", "true")]
    [TestCase("{\"foo\": -123.25}", "-123.25")]
    [TestCase("{\"foo\": null}", "null")]
    [TestCase("{\"foo\": 3.2e8}", "3.2e8")]
    public void Parse_QuotedField_CorrectField(string value, string expected)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var result = string.Empty;
        foreach (var c in value)
        {
            var state = parser.Parse(c);
            if (state == ParserState.Field || state == ParserState.Record)
                result = value.Substring(parser.ValueStart, parser.ValueLength);
        }
        Assert.That(result, Is.EqualTo(expected));
    }

    //[TestCase(@"{""foo"": ""\""bar\""""}")]
    //public void Parse_EscapeQuoteInQuotedField_EscapedSet(string value)
    //{
    //    var parser = new CharParser(NdjsonProfile.Default);
    //    foreach (var c in value)
    //        if (parser.Parse(c) == ParserState.Field)
    //        {
    //            Assert.That(parser.ValueStart, Is.EqualTo(11));
    //            Assert.That(parser.ValueLength, Is.EqualTo(3));
    //            Assert.That(parser.IsEscapedField, Is.True);
    //        }
    //}

    [TestCase("{\t \t\"foo\": \t \t\"bar\"}", 16)]
    public void Parse_SkipInitialSpace_SpaceSkip(string value, int start)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        foreach (var c in value)
            parser.Parse(c);
        Assert.That(value.Substring(parser.ValueStart, parser.ValueLength), Is.EqualTo("bar"));
    }
}
