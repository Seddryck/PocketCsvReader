using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing.CharParser;
public class NdjsonParserTest
{
    [TestCase("{\"foo\": \"bar\"}", 2, 3, 9, 3)]
    [TestCase("{\"foo\": 123}", 2, 3, 8, 3)]
    public void Parse_Field_StartEnd(string value, int startLabel, int lengthLabel, int startValue, int lengthValue)
    {
        var parser = new NdjsonParser(NdjsonProfile.Default.Dialect);
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));
        Assert.That(result, Is.EqualTo(ParserState.Record));

        result = parser.ParseEof(value.Length);
        Assert.That(result, Is.EqualTo(ParserState.Eof));
        Assert.That(parser.Result.Label.Start, Is.EqualTo(startLabel));
        Assert.That(parser.Result.Label.Length, Is.EqualTo(lengthLabel));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(startValue));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(lengthValue));
    }

    [TestCase("{\"foo\": \"bar\"}\r\n", "\r\n", 9, 3)]
    [TestCase("{\"foo\": \"bar\"}\n", "\n", 9, 3)]
    public void Parse_FieldLineTerminator_StartEnd(string value, string sep, int start, int length)
    {
        var parser = new NdjsonParser(new NdjsonProfile(sep).Dialect);
        var result = Enumerable
                        .Range(0, value.Length)
                        .Aggregate((ParserState?)null, (current, i) => parser.Parse(value[i], i));
        Assert.That(result, Is.EqualTo(ParserState.Continue));
        Assert.That(parser.Result.Value.Start, Is.EqualTo(start));
        Assert.That(parser.Result.Value.Length, Is.EqualTo(length));
    }

    [TestCase("{\"foo\": \"bar\"}", 1)]
    [TestCase("{\"foo\": 123, \"bar\": true}", 2)]
    [TestCase("{\"foo\": \"value\", \"bar\": true, \"qrz\": 123}", 3)]
    public void Parse_Record_CountOfField(string value, int count)
    {
        var parser = new NdjsonParser(NdjsonProfile.Default.Dialect);
        int actualCount = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var state = parser.Parse(value[i], i);
            if (state == ParserState.Record || state == ParserState.Field)
            {
                parser.Reset();
                actualCount++;
            }
        }
        Assert.That(actualCount, Is.EqualTo(count));
    }

    [TestCase("{\"foo\": \"bar\"}\r\n{\"foo\": \"qrz\"}", 2)]
    [TestCase("{\"foo\": \"bar\"}\r\n{\"foo\": \"qrz\"}\r\n", 2)]
    public void Parse_Record_CountOfRecord(string value, int count)
    {
        var parser = new NdjsonParser(NdjsonProfile.Default.Dialect);
        int actualCount = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var state = parser.Parse(value[i], i);
            if (state == ParserState.Record)
            {
                parser.Reset();
                actualCount++;
            }
        }
        actualCount += parser.ParseEof(value.Length) == ParserState.Record ? 1 : 0;
        Assert.That(actualCount, Is.EqualTo(count));
    }

    [TestCase("{\"foo\": \"bar\"}", "bar")]
    [TestCase("{\"foo\": 123}", "123")]
    [TestCase("{\"foo\": true}", "true")]
    [TestCase("{\"foo\": -123.25}", "-123.25")]
    [TestCase("{\"foo\": null}", "null")]
    [TestCase("{\"foo\": 3.2e8}", "3.2e8")]
    public void Parse_QuotedField_CorrectField(string value, string expected)
    {
        var parser = new NdjsonParser(NdjsonProfile.Default.Dialect);
        var result = string.Empty;
        for (var i = 0; i < value.Length; i++)
        {
            var state = parser.Parse(value[i], i);
            if (state == ParserState.Field || state == ParserState.Record)
                result = value.Substring(parser.Result.Value.Start, parser.Result.Value.Length);
        }
        Assert.That(result, Is.EqualTo(expected));
    }

    //[TestCase(@"{""foo"": ""\""bar\""""}")]
    //public void Parse_EscapeQuoteInQuotedField_EscapedSet(string value)
    //{
    //    var parser = new FieldParser(NdjsonProfile.Default);
    //    foreach (var c in value)
    //        if (parser.Parse(c) == ParserState.Field)
    //        {
    //            Assert.That(parser.ValueStart, Is.EqualTo(11));
    //            Assert.That(parser.Value.Length, Is.EqualTo(3));
    //            Assert.That(parser.IsEscapedField, Is.True);
    //        }
    //}

    //[TestCase("{\t \t\"foo\": \t \t\"bar\"}", 16)]
    //public void Parse_SkipInitialSpace_SpaceSkip(string value, int start)
    //{
    //    var parser = new NdjsonParser(NdjsonProfile.Default.Dialect);
    //    for (var i = 0; i < value.Length; i++)
    //        parser.Parse(value[i], i);

    //    Assert.That(value.Substring(parser.Result.Value.Start, parser.Result.Value.Length), Is.EqualTo("bar"));
    //}
}
