using PocketCsvReader.ValueReader;

namespace PocketCsvReader.Configuration;

/// <summary>Registers opt-in UTF-8 CSV value parsers.</summary>
public static class Utf8ParserExtensions
{
    /// <summary>Parses ISO dates directly from UTF-8 as midnight with an unspecified kind.</summary>
    /// <remarks>Other inputs use regular DateTime parsing. Explicit runtime parsers and field formats take precedence.</remarks>
    public static RuntimeParsersDescriptorBuilder UseIsoDateAsDateTime(this RuntimeParsersDescriptorBuilder parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        return parsers.WithDefaultUtf8Parser<DateTime>(TryParseAtMidnight);

        static bool TryParseAtMidnight(ReadOnlySpan<byte> utf8, out DateTime value)
        {
            if (IsoDateOnlyUtf8Parser.TryParse(utf8, out var date))
            {
                value = date.ToDateTime(TimeOnly.MinValue);
                return true;
            }

            value = default;
            return false;
        }
    }
}
