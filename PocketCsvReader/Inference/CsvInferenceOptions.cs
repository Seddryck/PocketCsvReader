using System.Globalization;

namespace PocketCsvReader;

/// <summary>Controls bounded schema and dialect inference.</summary>
public sealed record CsvInferenceOptions
{
    public int MaximumRecords { get; init; } = 1_000;
    public long? MaximumBytes { get; init; }
    public CsvHeaderMode Header { get; init; } = CsvHeaderMode.Auto;
    public bool InferDialect { get; init; }
    public IFormatProvider Culture { get; init; } = CultureInfo.InvariantCulture;
    public DialectDescriptor Dialect { get; init; } = new DialectDescriptor(Header: false, HeaderRows: []);
    public ResourceDescriptor? Resource { get; init; }
    public bool RestoreStreamPosition { get; init; } = true;
}

/// <summary>Specifies whether header presence is inferred or explicitly selected.</summary>
public enum CsvHeaderMode
{
    Auto,
    Present,
    Absent
}
