namespace PocketCsvReader;

// ReadLine discards terminators. The opt-in path captures them before discarding
// so offsets remain exact for LF, CRLF, CR, and unterminated final lines.
internal sealed class PositionedLineReader : IDisposable
{
    private readonly StreamReader _reader;
    private readonly BufferedRecordReader? _cursor;
    public PositionTracker? Tracker { get; }

    public PositionedLineReader(StreamReader reader, int bufferSize, bool trackPosition)
    {
        _reader = reader;
        if (trackPosition)
        {
            Tracker = new();
            _cursor = new(reader, bufferSize, tracker: Tracker);
        }
    }

    public string? ReadLine()
    {
        if (_cursor is null) return _reader.ReadLine();
        Tracker!.BeginRecord();
        _cursor.BeginOperation();
        return _cursor.ReadLineFrame()?.Memory.ToString();
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_cursor is null) return await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        Tracker!.BeginRecord();
        _cursor.BeginOperation();
        var frame = await _cursor.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame?.Memory.ToString();
    }

    public int Peek() => _cursor?.Peek() ?? _reader.Peek();
    public void Dispose() => _cursor?.Dispose();
}
