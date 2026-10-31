namespace PocketCsvReader.WebLogs;

/// <summary>A Common Log Format entry with typed values and nulls for missing fields.</summary>
public sealed record CommonLog(
    string? RemoteHost,
    string? Identity,
    string? AuthenticatedUser,
    DateTimeOffset? Timestamp,
    string? Request,
    int? StatusCode,
    long? ResponseBytes);
