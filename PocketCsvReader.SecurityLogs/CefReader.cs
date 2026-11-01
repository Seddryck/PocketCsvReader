namespace PocketCsvReader.SecurityLogs;

public sealed class CefReader : SecurityLogReader
{
    public CefReader() : this(new SecurityLogProfile(SecurityLogFormat.Cef)) { }

    public CefReader(SecurityLogProfile profile) : base(EnsureFormat(profile)) { }

    private static SecurityLogProfile EnsureFormat(SecurityLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == SecurityLogFormat.Cef ? profile
            : throw new ArgumentException("A Cef profile is required.", nameof(profile));
    }
}
