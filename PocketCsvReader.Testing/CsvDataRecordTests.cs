using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;
public class CsvDataRecordTests
{
    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetFieldValue_Parsable_Correct(string format, string input)
    {
        var culture = new CultureInfo("en-US");
        culture.DateTimeFormat.YearMonthPattern = format;

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue<YearMonth>(0, culture);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetFieldValue_NotParsable_Correct(string format, string input)
    {
        var culture = new CultureInfo("en-US");
        culture.DateTimeFormat.YearMonthPattern = format;

        BaseYearMonth parse(string input)
        {
            (int year, int month) = new YearMonthParser().Parse(input, culture);
            return new BaseYearMonth(year, month);
        }

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue(0, parse);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetValue_RegisteredGlobally_Parsable_Correct(string format, string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithTemporalField<YearMonth>(f => f.WithFormat(format)).Build()
        );

        IFormatProvider? getFormat(FieldDescriptor? field)
        {
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.DateTimeFormat.YearMonthPattern = format;
            return customCulture;
        };

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        record.Register<YearMonth>(getFormat);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetValue_RegisteredGlobally_NotParsable_Correct(string format, string input)
    {
        var culture = new CultureInfo("en-US");
        culture.DateTimeFormat.YearMonthPattern = format;

        BaseYearMonth parse(string input)
        {
            (int year, int month) = new YearMonthParser().Parse(input, culture);
            return new BaseYearMonth(year, month);
        }

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithField<BaseYearMonth>().Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        record.Register(parse);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-Qq", "2025-Q1")]
    [TestCase("yyyyQq", "2025Q1")]
    [TestCase("Qqq.yy", "Q01.25")]
    [TestCase("Qq'yy", "Q1'25")]
    public void GetFieldValue_CustomFormatProviderParsable_Correct(string format, string input)
    {
        var provider = new YearQuarterFormatProvider();
        provider.YearQuarterPattern = format;

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue<YearQuarter>(0, provider);
        Assert.That(value, Is.EqualTo(new YearQuarter(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-Qq", "2025-Q1")]
    [TestCase("yyyyQq", "2025Q1")]
    [TestCase("Qqq.yy", "Q01.25")]
    [TestCase("Qq'yy", "Q1'25")]
    public void GetFieldValue_CustomFormatProviderNotParsable_Correct(string format, string input)
    {
        var provider = new YearQuarterFormatProvider
        {
            YearQuarterPattern = format
        };

        BaseYearQuarter parse(string input)
        {
            (int year, int month) = new YearQuarterParser().Parse(input, provider);
            return new BaseYearQuarter(year, month);
        }

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue(0, parse);
        Assert.That(value, Is.EqualTo(new YearQuarter(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-Qq", "2025-Q1")]
    [TestCase("yyyyQq", "2025Q1")]
    [TestCase("Qqq.yy", "Q01.25")]
    [TestCase("Qq'yy", "Q1'25")]
    public void GetValue_CustomFormatProviderRegisteredLocally_Parsable_Correct(string format, string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithTemporalField<YearQuarter>(f => f.WithFormat(format)).Build()
        );

        IFormatProvider? getFormat(FieldDescriptor? field)
        {
            var provider = new YearQuarterFormatProvider
            {
                YearQuarterPattern = (field?.Format as TemporalFormatDescriptor)?.Pattern ?? "yyyy-Qq"
            };
            return provider;
        };

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        record.Register<YearQuarter>(getFormat);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearQuarter(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-Qq", "2025-Q1")]
    [TestCase("yyyyQq", "2025Q1")]
    [TestCase("Qqq.yy", "Q01.25")]
    [TestCase("Qq'yy", "Q1'25")]
    public void GetValue_CustomFormatProviderRegisteredGlobally_Parsable_Correct(string format, string input)
    {
        var provider = new YearQuarterFormatProvider
        {
            YearQuarterPattern = format
        };

        BaseYearQuarter parse(string input)
        {
            (int year, int quarter) = new YearQuarterParser().Parse(input, provider);
            return new BaseYearQuarter(year, quarter);
        }

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithField<BaseYearQuarter>().Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        record.Register(parse);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearQuarter(2025, 1)));
    }
}
