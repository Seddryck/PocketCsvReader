using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing.FieldParsing;
public class TypeParserLocatorTest
{
    [Test]
    [TestCase("Q1.25", "'Q'q.yy")]
    public void Locate_YearQuarterTwoParameters_FindIt(string input, string format)
    {
        var locator = new TypeParserLocator<Chrononuensis.YearQuarter>();
        var func = locator.Locate([format, CultureInfo.InvariantCulture]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(new Chrononuensis.YearQuarter(2025, 1)));
    }

    [Test]
    [TestCase("2025-Q1")]
    public void Locate_YearQuarterSingleParameter_FindIt(string input)
    {
        var locator = new TypeParserLocator<Chrononuensis.YearQuarter>();
        var func = locator.Locate([CultureInfo.InvariantCulture]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(new Chrononuensis.YearQuarter(2025, 1)));
    }
}
