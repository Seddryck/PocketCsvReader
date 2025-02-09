using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chrononuensis;
using Chrononuensis.Parsers;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;
public class CsvDataRecordTests
{
    [Test]
    [TestCase("en-us", "2025-12-26")]
    [TestCase("fr-fr", "26/12/2025")]
    [TestCase("de-de", "26.12.2025")]
    public void GetFieldValue_Parsable_Correct(string c, string input)
    {
        var culture = new CultureInfo(c);

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue<DateTime>(0, culture);
        Assert.That(value, Is.EqualTo(new DateTime(2025, 12, 26)));
    }

    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetFieldValue_WithFormat_Correct(string format, string input)
    {
        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]));
        var value = record.GetFieldValue<YearMonth>(0, format, CultureInfo.InvariantCulture);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetFieldValue_WithCustomFunction_Correct(string format, string input)
    {
        var culture = new CultureInfo("en-US");

        YearMonth parse(string input)
        {
            (int year, int month) = new YearMonthParser().Parse(input, format, culture);
            return new YearMonth(year, month);
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
    public void GetValue_RegisteredGlobally_NotParsable_Correct(string format, string input)
    {
        YearMonth parse(string input)
        {
            (int year, int month) = new YearMonthParser().Parse(input, format, CultureInfo.InvariantCulture);
            return new YearMonth(year, month);
        }

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder()
                    .Indexed()
                    .WithField<YearMonth>()
                    .Build()
            , null
            , new RuntimeParsersDescriptorBuilder()
                    .WithParser(parse)
                    .Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }


    [Test]
    [TestCase("yyyy-MM", "2025-01")]
    [TestCase("yyyy-M", "2025-1")]
    [TestCase("yyyy.MM", "2025.01")]
    [TestCase("MM.yyyy", "01.2025")]
    public void GetValue_AutoDiscoveryParsable_Correct(string format, string input)
    {
        var culture = new CultureInfo("en-US");
        culture.DateTimeFormat.YearMonthPattern = format;

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithTemporalField<YearMonth>(
                    tf => tf.WithFormat(format)
                ).Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }

    [Test]
    [TestCase("J25")]
    public void GetValue_RegisteredWithBuilderNotParsable_Correct(string input)
    {
        YearMonth parse(string input)
        {
            if (input[0] == 'J' && input.EndsWith("25"))
                return new YearMonth(2025, 1);
            throw new ArgumentException();
        }

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithTemporalField<YearMonth>(
                    tf => tf.WithParser((str) => parse(str))
                ).Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, input.Length)]), profile);
        var value = record.GetValue(0);
        Assert.That(value, Is.EqualTo(new YearMonth(2025, 1)));
    }


    [Test]
    [TestCase("J25;F25")]
    public void GetValue_RegisteredWithManyBuilderNotParsable_Correct(string input)
    {
        YearMonth parseAlpha(string input)
        {
            if (input[0] == 'J' && input.EndsWith("25"))
                return new YearMonth(2025, 1);
            throw new ArgumentException();
        }

        YearMonth parseBeta(string input)
        {
            if (input[0] == 'F' && input.EndsWith("25"))
                return new YearMonth(2025, 2);
            throw new ArgumentException();
        }

        var profile = new CsvProfile(
            new DialectDescriptorBuilder().WithDelimiter(';').Build()
            , new SchemaDescriptorBuilder().Indexed()
                .WithTemporalField<YearMonth>(
                    tf => tf.WithParser((str) => parseAlpha(str))
                )
                .WithTemporalField<YearMonth>(
                    tf => tf.WithParser((str) => parseBeta(str))
                ).Build()
        );

        var record = new CsvDataRecord(new RecordMemory(input, [new FieldSpan(0, 3), new FieldSpan(4,3)]), profile);
        Assert.That(record.GetValue(0), Is.EqualTo(new YearMonth(2025, 1)));
        Assert.That(record.GetValue(1), Is.EqualTo(new YearMonth(2025, 2)));
    }
}
