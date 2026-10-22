namespace PocketCsvReader;

internal interface IPositionSource
{
    PositionTracker? Tracker { get; }
}

// One optional instance per reader; snapshots are values, never boxed or allocated per record.
internal sealed class PositionTracker(PositionOffsetUnit unit = PositionOffsetUnit.Utf16CodeUnits)
{
    public long Offset { get; private set; }
    public long PhysicalLine { get; private set; } = 1;
    public long RecordNumber { get; private set; }
    public RecordPosition? CurrentPosition { get; private set; }
    private long _startOffset, _startLine;
    private bool _previousCr, _startPreviousCr;

    public void BeginRecord()
    {
        _startOffset = Offset;
        _startLine = PhysicalLine;
        _startPreviousCr = _previousCr;
        CurrentPosition = null;
    }

    public RecordPosition PendingPosition => new(RecordNumber + 1, _startLine, _startOffset, unit);

    public void CompleteRecordAt(RecordPosition start, ReadOnlySpan<char> prefix)
    {
        long line = start.PhysicalLine;
        for (int i = 0; i < prefix.Length; i++)
            if (prefix[i] == '\r' || prefix[i] == '\n' && (i == 0 ? !_startPreviousCr : prefix[i - 1] != '\r')) line++;
        CurrentPosition = new(++RecordNumber, line, start.Offset + prefix.Length, unit);
    }

    public void CompleteRecord()
        => CurrentPosition = new(++RecordNumber, _startLine, _startOffset, unit);

    public void SkipRecord() { RecordNumber++; CurrentPosition = null; }

    public static void ExcludeRecord<T>(IRecordSource<T> source)
    {
        if (source is IPositionSource { Tracker: { } tracker } && tracker.CurrentPosition.HasValue)
        {
            tracker.RecordNumber--;
            tracker.CurrentPosition = null;
        }
    }

    public void Advance(char value)
    {
        Offset++;
        if (value == '\r' || value == '\n' && !_previousCr) PhysicalLine++;
        _previousCr = value == '\r';
    }

    public void Advance(ReadOnlySpan<char> input)
    {
        if (input.IsEmpty) return;
        Offset += input.Length;
        int consumed = 0;
        while (true)
        {
            int next = input[consumed..].IndexOfAny('\r', '\n');
            if (next < 0) break;
            int index = consumed + next;
            char value = input[index];
            bool followsCr = index == 0 ? _previousCr : input[index - 1] == '\r';
            if (value == '\r' || !followsCr) PhysicalLine++;
            consumed = index + 1;
        }
        _previousCr = input[^1] == '\r';
    }

    public void Advance(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty) return;
        Offset += input.Length;
        int consumed = 0;
        while (true)
        {
            int next = input[consumed..].IndexOfAny((byte)'\r', (byte)'\n');
            if (next < 0) break;
            int index = consumed + next;
            byte value = input[index];
            bool followsCr = index == 0 ? _previousCr : input[index - 1] == '\r';
            if (value == '\r' || !followsCr) PhysicalLine++;
            consumed = index + 1;
        }
        _previousCr = input[^1] == '\r';
    }
}
