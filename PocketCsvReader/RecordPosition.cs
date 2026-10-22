namespace PocketCsvReader;

/// <summary>The unit used by a parser's source offset.</summary>
public enum PositionOffsetUnit
{
    Utf16CodeUnits,
    Bytes
}

/// <summary>An immutable record-start position, relative to parser input after its initial BOM.</summary>
public readonly record struct RecordPosition(
    long RecordNumber,
    long PhysicalLine,
    long Offset,
    PositionOffsetUnit OffsetUnit);
