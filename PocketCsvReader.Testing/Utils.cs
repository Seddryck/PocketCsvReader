using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Testing;
internal class YearMonthParser
{
    public (int Year, int Month) Parse(string input, IFormatProvider? formatProvider = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or empty.", nameof(input));

        var dateTimeFormat = formatProvider as DateTimeFormatInfo ?? (formatProvider as CultureInfo ?? CultureInfo.InvariantCulture).DateTimeFormat;
        var format = dateTimeFormat.YearMonthPattern;

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

                if (inputIndex + yearLength > input.Length || !int.TryParse(input.Substring(inputIndex, yearLength), NumberStyles.Integer, dateTimeFormat, out year))
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

                if (inputIndex + monthLength > input.Length || !int.TryParse(input.Substring(inputIndex, monthLength), NumberStyles.Integer, dateTimeFormat, out month))
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

internal class BaseYearMonth : IEquatable<BaseYearMonth>
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

internal class YearMonth : BaseYearMonth, IParsable<YearMonth>
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

internal class BaseYearQuarter : IEquatable<BaseYearQuarter>
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

internal class YearQuarter : BaseYearQuarter, IParsable<YearQuarter>
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

internal class YearQuarterFormatProvider : IFormatProvider, ICustomFormatter
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
internal class YearQuarterParser
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

