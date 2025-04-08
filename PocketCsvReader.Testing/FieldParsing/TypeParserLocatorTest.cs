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

    [Test]
    [TestCase("10-12-2025")]
    public void Locate_DateTime_FindIt(string input)
    {
        var locator = new TypeParserLocator<DateTime>();
        var func = locator.Locate(["dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(new DateTime(2025, 12, 10)));
    }

    [Test]
    [TestCase("10-12-2025")]
    public void Locate_DateOnly_FindIt(string input)
    {
        var locator = new TypeParserLocator<DateOnly>();
        var func = locator.Locate(["dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(new DateOnly(2025, 12, 10)));
    }

    [Test]
    [TestCase("165")]
    public void Locate_Int32_FindIt(string input)
    {
        var locator = new TypeParserLocator<int>();
        var func = locator.Locate([NumberStyles.Integer, CultureInfo.InvariantCulture]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(165));
    }

    [Test]
    [TestCase("165")]
    public void Locate_Int32AsTemporal_FindIt(string input)
    {
        var locator = new TypeParserLocator<int>();
        var func = locator.Locate(["dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo(165));
    }

    [Test]
    [TestCase("165")]
    public void Locate_Int16_FindIt(string input)
    {
        var locator = new TypeParserLocator<short>();
        var func = locator.Locate([NumberStyles.Integer, CultureInfo.InvariantCulture]);
        var value = func.Invoke(input);
        Assert.That(value, Is.EqualTo((short)165));
    }
}
