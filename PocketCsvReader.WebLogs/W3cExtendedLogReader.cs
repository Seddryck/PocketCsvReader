namespace PocketCsvReader.WebLogs;

public sealed class W3cExtendedLogReader : WebLogReader
{
    public W3cExtendedLogReader()
        : this(new WebLogProfile(WebLogFormat.W3cExtended))
    { }

    public W3cExtendedLogReader(WebLogProfile profile)
        : base(EnsureFormat(profile))
    { }

    private static WebLogProfile EnsureFormat(WebLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == WebLogFormat.W3cExtended
            ? profile
            : throw new ArgumentException("A W3C Extended Log Format profile is required.", nameof(profile));
    }
}
