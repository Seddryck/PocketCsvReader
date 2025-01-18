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
public class CsvDataRecordTest
{
    private class YearMonthParser
    {
        public (int Year, int Month) Parse(string input, IFormatProvider? formatProvider = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be null or empty.", nameof(input));

            var culture = formatProvider as CultureInfo ?? CultureInfo.InvariantCulture;
            var format = culture.DateTimeFormat.YearMonthPattern;

            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Format cannot be null or empty.", nameof(format));

            int year = 0, month = 0;

            for (int i = 0, inputIndex = 0; i < format.Length; i++)
            {
                char formatChar = format[i];

                if (char.ToLowerInvariant(formatChar) == 'y')
                {
                    int yearLength = 0;
                    while (i < format.Length && char.ToLowerInvariant(format[i]) == 'y')
                    {
                        yearLength++;
                        i++;
                    }
                    i--;

                    if (inputIndex + yearLength > input.Length || !int.TryParse(input.Substring(inputIndex, yearLength), NumberStyles.Integer, culture, out year))
                        throw new FormatException("Year part is not valid or too short.");

                    inputIndex += yearLength;
                }
                else if (char.ToLowerInvariant(formatChar) == 'm')
                {
                    int monthLength = 0;
                    while (i < format.Length && char.ToLowerInvariant(format[i]) == 'm')
                    {
                        monthLength++;
                        i++;
                    }
                    i--;

                    if (inputIndex + monthLength > input.Length || !int.TryParse(input.Substring(inputIndex, monthLength), NumberStyles.Integer, culture, out month))
                        throw new FormatException("Month part is not valid or too short.");

                    inputIndex += monthLength;
                }
                else
                {
                    // Ensure input matches non-variable characters in the format
                    if (inputIndex >= input.Length || input[inputIndex] != formatChar)
                        throw new FormatException("Input does not match the expected format.");

                    inputIndex++;
                }
            }

            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

            return (year, month);
        }
    }

    private class BaseYearMonth : IEquatable<BaseYearMonth>
    {
        public int Year { get; }
        public int Month { get; }
        public BaseYearMonth(int year, int month)
        {
            Year = year;
            Month = month;
        }
        public override bool Equals(object? obj)
            => obj is not null && obj is YearMonth other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Year, Month);

        public bool Equals(BaseYearMonth? other)
            => other is not null && Year == other.Year && Month == other.Month;
    }

    private class YearMonth : BaseYearMonth, IParsable<YearMonth>
    {
        private static YearMonthParser parser = new();

        public YearMonth(int year, int month)
            : base(year, month)
        { }

        public static YearMonth Parse(string input, IFormatProvider? provider)
        {
            (int year, int month) = parser.Parse(input, provider);
            return new YearMonth(year, month);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out YearMonth result)
            => throw new NotImplementedException();
    }

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
    public void GetValue_RegisteredLocally_Parsable_Correct(string format, string input)
    {
        var profile = new CsvProfile(
            new DialectDescriptorBuilder().Build()
            , new SchemaDescriptorBuilder().Indexed().WithField<YearMonth>(f => f.WithFormat(format)).Build()
        );

        CultureInfo? getFormat(string? fmt)
        {
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.DateTimeFormat.YearMonthPattern = string.IsNullOrEmpty(fmt) ? "yyyy-MM" : fmt;
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
    public void GetValue_RegisteredGlobally_Parsable_Correct(string format, string input)
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

    private class BaseYearQuarter : IEquatable<BaseYearQuarter>
    {
        public int Year { get; }
        public int Quarter { get; }
        public BaseYearQuarter(int year, int quarter)
        {
            Year = year;
            Quarter = quarter;
        }
        public override bool Equals(object? obj)
            => obj is not null && obj is BaseYearQuarter other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Year, Quarter);

        public bool Equals(BaseYearQuarter? other)
            => other is not null && Year == other.Year && Quarter == other.Quarter;
    }

    private class YearQuarter : BaseYearQuarter, IParsable<YearQuarter>
    {
        private static YearQuarterParser parser = new();

        public YearQuarter(int year, int month)
            : base(year, month)
        { }

        public static YearQuarter Parse(string input, IFormatProvider? provider)
        {
            (int year, int quarter) = parser.Parse(input, provider);
            return new YearQuarter(year, quarter);
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out YearQuarter result)
            => throw new NotImplementedException();
    }

    private class YearQuarterFormatProvider : IFormatProvider, ICustomFormatter
    {
        public string YearQuarterPattern { get; set; } = "yyyy-Qq";

        public object? GetFormat(Type? formatType)
        {
            return formatType == typeof(ICustomFormatter) ? this : null;
        }

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            if (arg == null || string.IsNullOrWhiteSpace(format))
                return string.Empty;

            format ??= YearQuarterPattern;

            if (arg is (int Year, int Quarter))
            {
                format = format.Replace("yyyy", Year.ToString("D4"))
                               .Replace("yy", (Year % 100).ToString("D2"))
                               .Replace("Q", $"Q{Quarter}");
                return format;

            }

            throw new FormatException("Unsupported format or argument type.");
        }
    }

    private class YearQuarterParser
    {
        public (int Year, int Quarter) Parse(string input, IFormatProvider? formatProvider = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be null or empty.", nameof(input));

            var format = formatProvider is YearQuarterFormatProvider provider
                ? provider.YearQuarterPattern
                : "yyyy-Qq";

            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Format cannot be null or empty.", nameof(format));

            int year = 0, quarter = 0;

            for (int i = 0, inputIndex = 0; i < format.Length; i++)
            {
                char formatChar = format[i];

                if (char.ToLowerInvariant(formatChar) == 'y')
                {
                    int yearLength = 0;
                    while (i < format.Length && char.ToLowerInvariant(format[i]) == 'y')
                    {
                        yearLength++;
                        i++;
                    }
                    i--;

                    if (inputIndex + yearLength > input.Length || !int.TryParse(input.Substring(inputIndex, yearLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                        throw new FormatException("Year part is not valid or too short.");

                    inputIndex += yearLength;
                }
                else if (formatChar == 'q')
                {
                    int quarterLength = 0;
                    while (i < format.Length && format[i] == 'q')
                    {
                        quarterLength++;
                        i++;
                    }
                    i--;

                    if (inputIndex + quarterLength > input.Length || !int.TryParse(input.Substring(inputIndex, quarterLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out quarter))
                        throw new FormatException("Quarter part is not valid or too short.");

                    inputIndex += quarterLength;
                }
                else
                {
                    // Ensure input matches non-variable characters in the format
                    if (inputIndex >= input.Length || input[inputIndex] != formatChar)
                        throw new FormatException("Input does not match the expected format.");

                    inputIndex++;
                }
            }

            if (quarter < 1 || quarter > 4)
                throw new ArgumentOutOfRangeException(nameof(quarter), "Quater must be between 1 and 4.");

            if (year < 50)
                year += 2000;
            else if (year < 100)
                year += 1900;

            return (year, quarter);
        }
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
            , new SchemaDescriptorBuilder().Indexed().WithField<YearQuarter>(f => f.WithFormat(format)).Build()
        );

        IFormatProvider? getFormat(string? fmt)
        {
            var provider = new YearQuarterFormatProvider
            {
                YearQuarterPattern = format
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
