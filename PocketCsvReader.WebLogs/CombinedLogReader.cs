using System.Globalization;

namespace PocketCsvReader.WebLogs;

public sealed class CombinedLogReader : WebLogReader
{
    public CombinedLogReader()
        : this(new WebLogProfile(WebLogFormat.Combined))
    { }

    public CombinedLogReader(WebLogProfile profile)
        : base(EnsureFormat(profile))
    { }

    /// <summary>Streams typed Combined entries from a file, closing the file when enumeration ends.</summary>
    public IEnumerable<CombinedLog> ToCombinedLog(string filename)
        => To<CombinedLog>(filename, MapCombinedLog);

    /// <summary>Streams typed Combined entries, disposing the input stream when enumeration ends.</summary>
    public IEnumerable<CombinedLog> ToCombinedLog(Stream stream)
        => To<CombinedLog>(stream, MapCombinedLog);

    private static CombinedLog MapCombinedLog(WebLogDataReader reader)
        => new(
            reader.GetFieldValue<string?>(0),
            reader.GetFieldValue<string?>(1),
            reader.GetFieldValue<string?>(2),
            reader.GetFieldValue<DateTimeOffset?>(3, (ReadOnlySpan<char> value) =>
                DateTimeOffset.ParseExact(value, "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture)),
            GetOptionalText(reader, 4),
            reader.GetFieldValue<int?>(5, (ReadOnlySpan<char> value) =>
                int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            reader.GetFieldValue<long?>(6, (ReadOnlySpan<char> value) =>
                long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            GetOptionalText(reader, 7),
            GetOptionalText(reader, 8));

    private static string? GetOptionalText(WebLogDataReader reader, int ordinal)
        => reader.GetFieldValue<string?>(ordinal) is { } value && value != "-" ? value : null;

    private static WebLogProfile EnsureFormat(WebLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == WebLogFormat.Combined
            ? profile
            : throw new ArgumentException("A Combined Log Format profile is required.", nameof(profile));
    }
}
