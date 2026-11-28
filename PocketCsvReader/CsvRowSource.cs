namespace PocketCsvReader;

/// <summary>Identifies a CSV batch row within its original input.</summary>
/// <param name="InputIndex">The zero-based index of the input.</param>
/// <param name="FileName">The filename supplied by the caller, if available.</param>
/// <param name="RecordNumber">The one-based data record number within the input.</param>
/// <param name="Position">The record start when position tracking is enabled.</param>
public readonly record struct CsvRowSource(
    int InputIndex, string? FileName, long RecordNumber, RecordPosition? Position);
