namespace PocketCsvReader;

/// <summary>Configures a generated, one-based integer column before the source columns.</summary>
public sealed record RowNumberOptions(string ColumnName = "rowNumber")
{
    internal void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(ColumnName);
}
