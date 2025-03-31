using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing.CharParsing;
public class CharOfLabelParserTests
{
    [Test]
    [TestCase('f')]
    [TestCase('1')]
    [TestCase('\"')]
    public void Parse_Expected_Continue(char value)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new CharOfLabelParser(parser);
        Assert.That(intern.Parse(value), Is.EqualTo(ParserState.Continue));
    }

    [Test]
    [TestCase('f', 0)]
    [TestCase('1', 0)]
    [TestCase('\"', -1)]
    public void Parse_Expected_SetLabel(char value, int position)
    {
        var parser = new CharParser(NdjsonProfile.Default);
        var intern = new CharOfLabelParser(parser);
        intern.Parse(value);
        Assert.That(parser.LabelLength, Is.EqualTo(position));
    }
}
