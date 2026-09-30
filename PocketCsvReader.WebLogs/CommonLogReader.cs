namespace PocketCsvReader.WebLogs;

public sealed class CommonLogReader : WebLogReader
{
    public CommonLogReader()
        : this(new WebLogProfile(WebLogFormat.Common))
    { }

    public CommonLogReader(WebLogProfile profile)
        : base(EnsureFormat(profile))
    { }

    private static WebLogProfile EnsureFormat(WebLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == WebLogFormat.Common
            ? profile
            : throw new ArgumentException("A Common Log Format profile is required.", nameof(profile));
    }
}
