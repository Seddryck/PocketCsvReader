using System.Globalization;

namespace PocketCsvReader.WebLogs;

public sealed class CommonLogReader : WebLogReader
{
    public CommonLogReader()
        : this(new WebLogProfile(WebLogFormat.Common))
    { }

    public CommonLogReader(WebLogProfile profile)
        : base(EnsureFormat(profile))
    { }

    /// <summary>Streams typed CLF entries from a file, closing the file when enumeration ends.</summary>
    public IEnumerable<CommonLog> ToCommonLog(string filename)
        => To<CommonLog>(filename, MapCommonLog);

    /// <summary>Streams typed CLF entries, disposing the input stream when enumeration ends.</summary>
    public IEnumerable<CommonLog> ToCommonLog(Stream stream)
        => To<CommonLog>(stream, MapCommonLog);

    private static CommonLog MapCommonLog(WebLogDataReader reader)
        => new(
            reader.GetFieldValue<string?>(0),
            reader.GetFieldValue<string?>(1),
            reader.GetFieldValue<string?>(2),
            reader.GetFieldValue<DateTimeOffset?>(3, (ReadOnlySpan<char> value) =>
                DateTimeOffset.ParseExact(value, "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture)),
            reader.GetFieldValue<string?>(4) is { } request && request != "-" ? request : null,
            reader.GetFieldValue<int?>(5, (ReadOnlySpan<char> value) =>
                int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            reader.GetFieldValue<long?>(6, (ReadOnlySpan<char> value) =>
                long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)));

    private static WebLogProfile EnsureFormat(WebLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == WebLogFormat.Common
            ? profile
            : throw new ArgumentException("A Common Log Format profile is required.", nameof(profile));
    }
}
