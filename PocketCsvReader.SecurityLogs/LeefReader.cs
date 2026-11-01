namespace PocketCsvReader.SecurityLogs;

public sealed class LeefReader : SecurityLogReader
{
    public LeefReader() : this(new SecurityLogProfile(SecurityLogFormat.Leef)) { }

    public LeefReader(SecurityLogProfile profile) : base(EnsureFormat(profile)) { }

    private static SecurityLogProfile EnsureFormat(SecurityLogProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Format == SecurityLogFormat.Leef ? profile
            : throw new ArgumentException("A Leef profile is required.", nameof(profile));
    }
}
