using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing.CharParsing;
public class QuotedCharOfLabelParserTests
{
    [Test]
    [TestCase('\"')]
    [TestCase(' ')]
    [TestCase('\t')]
    public void Parse_Expected_Continue(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new QuotedCharOfLabelParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Continue));
    }

    [Test]
    [TestCase('\\')]
    [TestCase('f')]
    public void Parse_Expected_Error(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new QuotedCharOfLabelParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Error));
    }

    [Test]
    [TestCase(' ', '\"')]
    public void Parse_ExpectedChain_Continue(params char[] values)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new QuotedCharOfLabelParser(parser);
        foreach (var value in values)
            Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Continue));
    }
}
