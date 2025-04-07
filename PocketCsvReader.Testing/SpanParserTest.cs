using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class SpanParserTest
{
    [Test]
    public void Parse_Integer_ReturnsExpected()
    {
        var span = new ReadOnlySpan<char>("125".ToArray());
        var parser = SpanParser.Default;
        var result = parser.Parse<int>(span);
        Assert.That(result, Is.EqualTo(125));
    }

    [Test]
    public void Parse_DateTime_ReturnsExpected()
    {
        var span = new ReadOnlySpan<char>("2025-04-06T17:47:52".ToArray());
        var parser = SpanParser.Default;
        var result = parser.Parse<DateTime>(span);
        Assert.That(result, Is.EqualTo(new DateTime(2025,4,6,17,47,52)));
    }

    [Test]
    public void Parse_Decimal_ReturnsExpected()
    {
        var span = new ReadOnlySpan<char>("125.17".ToArray());
        var parser = SpanParser.Default;
        var result = parser.Parse<decimal>(span);
        Assert.That(result, Is.EqualTo(125.17m));
    }

    [Test]
    public void Parse_CustomTypeParser_ReturnsExpected()
    {
        var span = new ReadOnlySpan<char>("125,17".ToArray());
        var parser = new SpanParser();
        parser.Register(s =>
        {
            var parts = s.ToString().Split(',');
            return decimal.Parse(parts[0]) + decimal.Parse(parts[1]) / 100;
        });
        var result = parser.Parse<decimal>(span);
        Assert.That(result, Is.EqualTo(125.17m));
    }

    [Test]
    public void Parse_CustomTypeParserAndFieldParser_ReturnsExpected()
    {
        var span = new ReadOnlySpan<char>("125#17".ToArray());
        var parser = new SpanParser();
        parser.Register(s =>
        {
            var parts = s.ToString().Split(',');
            return decimal.Parse(parts[0]) + decimal.Parse(parts[1]) / 100;
        });
        parser.Register(1, s =>
        {
            var parts = s.ToString().Split('#');
            return decimal.Parse(parts[1]) + decimal.Parse(parts[0]) / 1000;
        });
        var result = parser.Parse<decimal>(1, span);
        Assert.That(result, Is.EqualTo(17.125m));
    }
}
