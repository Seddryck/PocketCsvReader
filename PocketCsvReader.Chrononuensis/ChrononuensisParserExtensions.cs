using System.Globalization;
using Chrononuensis;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Chrononuensis;

/// <summary>Registers the public Chrononuensis span parsers as overridable defaults.</summary>
public static class ChrononuensisParserExtensions
{
    /// <summary>Enables all ten supported temporal types, honoring field formats and cultures.</summary>
    /// <remarks>Explicit WithParser registrations take precedence in either registration order.
    /// Fields without a culture use <paramref name="provider"/> or invariant culture.</remarks>
    public static RuntimeParsersDescriptorBuilder UseChrononuensis(
        this RuntimeParsersDescriptorBuilder options, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        provider ??= CultureInfo.InvariantCulture;
        Register<MonthDay>(options, MonthDay.Parse, MonthDay.Parse, provider);
        Register<YearDay>(options, YearDay.Parse, YearDay.Parse, provider);
        Register<YearWeek>(options, YearWeek.Parse, YearWeek.Parse, provider);
        Register<YearMonth>(options, YearMonth.Parse, YearMonth.Parse, provider);
        Register<YearQuarter>(options, YearQuarter.Parse, YearQuarter.Parse, provider);
        Register<YearSemester>(options, YearSemester.Parse, YearSemester.Parse, provider);
        Register<Year>(options, Year.Parse, Year.Parse, provider);
        Register<Olympiad>(options, Olympiad.Parse, Olympiad.Parse, provider);
        Register<Decade>(options, Decade.Parse, Decade.Parse, provider);
        Register<Century>(options, Century.Parse, Century.Parse, provider);
        return options;
    }

    private delegate T ParseTemporal<T>(ReadOnlySpan<char> input, IFormatProvider? provider);
    private delegate T ParseFormattedTemporal<T>(ReadOnlySpan<char> input, string format, IFormatProvider? provider);

    private static void Register<T>(RuntimeParsersDescriptorBuilder options, ParseTemporal<T> parse,
        ParseFormattedTemporal<T> formatted, IFormatProvider provider)
        => options.WithDefaultParser<T>(input => parse(input, provider),
            (pattern, culture) => pattern is null
                ? input => parse(input, culture ?? provider)
                : input => formatted(input, pattern, culture ?? provider));
}
