using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing.CharParsing;
public class FirstCharOfValueParserTests
{
    [Test]
    [TestCase('f')]
    [TestCase('1')]
    [TestCase('\"')]
    [TestCase(' ')]
    public void Parse_Expected_Continue(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfValueParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Continue));
    }

    [Test]
    [TestCase('f', false)]
    [TestCase('1', false)]
    [TestCase('\"', true)]
    [TestCase(' ', false)]
    public void Parse_Expected_IsQuoted(char value, bool expected)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfValueParser(parser);
        intern.Parse(value);
        Assert.That(parser.IsQuotedField, Is.EqualTo(expected));
    }

    [Test]
    [TestCase('f', 1)]
    [TestCase('1', 1)]
    [TestCase('\"', 0)]
    [TestCase(' ', 0)]
    public void Parse_Expected_FieldLength(char value, int expected)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfValueParser(parser);
        intern.Parse(value);
        Assert.That(parser.ValueLength, Is.EqualTo(expected));
    }

    [Test]
    [TestCase('}')]
    [TestCase(',')]
    [TestCase(':')]
    public void Parse_Expected_SetLabel(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfValueParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Error));
    }
}
