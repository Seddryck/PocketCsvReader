using System.Text;

namespace PocketCsvReader;

internal interface IPositionSource
{
    PositionTracker? Tracker { get; }
}

// One optional instance per reader; snapshots are values, never boxed or allocated per record.
internal sealed class PositionTracker(PositionOffsetUnit unit = PositionOffsetUnit.Utf16CodeUnits, bool countUtf8 = false)
{
    public long Offset { get; private set; }
    public long PhysicalLine { get; private set; } = 1;
    public long RecordNumber { get; private set; }
    public RecordPosition? CurrentPosition { get; private set; }
    private long _startOffset;
    private long _startLine;
    private bool _previousCr;
    private bool _startPreviousCr;
    private bool _previousHighSurrogate;

    public static PositionTracker? ForTextReader(StreamReader reader, bool enabled)
        => reader is Utf8FileStreamReader { TrackByteOffsets: true }
            ? new(PositionOffsetUnit.Bytes, countUtf8: true)
            : enabled && reader is not Utf8FileStreamReader { Restarted: true } ? new() : null;

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
            if (prefix[i] == '\r' || (prefix[i] == '\n' && (i == 0 ? !_startPreviousCr : prefix[i - 1] != '\r'))) line++;
        var length = countUtf8 ? Encoding.UTF8.GetByteCount(prefix) : prefix.Length;
        CurrentPosition = new(++RecordNumber, line, start.Offset + length, unit);
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
        if (countUtf8)
            AdvanceUtf8Offset(new ReadOnlySpan<char>(in value));
        else
            Offset++;
        if (value == '\r' || (value == '\n' && !_previousCr)) PhysicalLine++;
        _previousCr = value == '\r';
    }

    public void Advance(ReadOnlySpan<char> input)
    {
        if (input.IsEmpty) return;
        if (countUtf8)
            AdvanceUtf8Offset(input);
        else
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

    private void AdvanceUtf8Offset(ReadOnlySpan<char> input)
    {
        Offset += Encoding.UTF8.GetByteCount(input);
        // A surrogate pair split across buffers counts as two three-byte replacements.
        // Its original UTF-8 encoding is four bytes.
        if (_previousHighSurrogate && char.IsLowSurrogate(input[0]))
            Offset -= 2;
        _previousHighSurrogate = char.IsHighSurrogate(input[^1]);
    }
}
