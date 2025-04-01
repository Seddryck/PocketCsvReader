using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing.CharParsing;
public class FirstCharOfLabelParserTests
{
    [Test]
    [TestCase('f')]
    [TestCase('1')]
    public void Parse_Expected_Continue(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfLabelParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Continue));
    }

    [Test]
    [TestCase('f', -1, 1)]
    [TestCase('1', -1, 1)]
    [TestCase('\"', 0, 1)]
    [TestCase(' ', 0, 0)]
    public void Parse_Expected_FieldLength(char value, int start, int length)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new FirstCharOfValueParser(parser);
        intern.Parse(value);
        Assert.That(parser.ValueStart, Is.EqualTo(start));
        Assert.That(parser.ValueLength, Is.EqualTo(length));
    }
}
