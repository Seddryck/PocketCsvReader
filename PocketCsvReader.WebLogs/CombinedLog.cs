namespace PocketCsvReader.WebLogs;

/// <summary>A Combined Log Format entry with typed values and nulls for missing fields.</summary>
public sealed record CombinedLog(
    string? RemoteHost,
    string? Identity,
    string? AuthenticatedUser,
    DateTimeOffset? Timestamp,
    string? Request,
    int? StatusCode,
    long? ResponseBytes,
    string? Referrer,
    string? UserAgent);
