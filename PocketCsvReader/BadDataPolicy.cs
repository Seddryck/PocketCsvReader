namespace PocketCsvReader;

public enum BadDataAction { Throw,
    SkipRecord,
    ReturnPartialRecord,
    Stop }

public sealed record BadDataContext(long RecordNumber, long PhysicalLine, int? FieldIndex,
    long Offset, ParserState ParserState, string OffendingInput, Exception Exception)
{
    /// <summary>The precise record start when position tracking is enabled.</summary>
    /// <value>The source position, or null when tracking is disabled.</value>
    public RecordPosition? RecordPosition { get; init; }
    /// <summary>Unit of Offset; the legacy character path also uses UTF-16 units.</summary>
    /// <value>The unit used by Offset.</value>
    public PositionOffsetUnit OffsetUnit { get; init; } = PositionOffsetUnit.Utf16CodeUnits;
}

public sealed record BadDataPolicy
{
    public Func<BadDataContext, BadDataAction> Handler { get; init; } = _ => BadDataAction.Throw;
    public int MaximumErrors { get; init; } = 1;

    public BadDataPolicy() { }

    public BadDataPolicy(Func<BadDataContext, BadDataAction> handler, int maximumErrors = 1)
    {
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (maximumErrors <= 0) throw new ArgumentOutOfRangeException(nameof(maximumErrors));
        MaximumErrors = maximumErrors;
    }
}
