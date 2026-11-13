namespace PocketCsvReader.Markdown;

internal sealed class MarkdownRecordSource : IBorrowedRecordSource<MarkdownProfile>
{
    private static readonly CurrentRecordResult EndOfFile = new(true, default, RecordState.Eof);
    private readonly BufferedRecordReader _reader;
    private int _tablesToSkip;
    private ScanState _state;
    private Line? _pending;
    private long _physicalLine;
    private long _recordNumber;
    private int _errorCount;
    // A selected table has a fixed width. This array and the line capture are borrowed until the next read.
    private FieldSpan[] _fields = [];

    public MarkdownProfile Profile { get; }
    public string[]? Header { get; private set; }
    public RecordPosition? CurrentPosition { get; private set; }

    public MarkdownRecordSource(StreamReader reader, MarkdownProfile profile)
    {
        Profile = profile;
        _reader = new(reader, profile.ParserOptimizations.BufferSize);
        _tablesToSkip = profile.SkipTables;
        _state = profile.TextMarker is null ? ScanState.FindTable : ScanState.FindMarker;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        var result = ReadCurrent();
        record = RecordSpan.FromMemory(result.Record.Characters, result.Record.Fields ?? []);
        recordState = result.State;
        return result.IsEndOfFile;
    }

    CurrentRecordResult ICurrentRecordSource.ReadCurrent() => ReadCurrent();

    private CurrentRecordResult ReadCurrent()
    {
        CurrentPosition = null;
        while (_state != ScanState.Finished)
        {
            _reader.BeginOperation(_pending?.Capture);
            long offset = _reader.Position;
            if (ProcessLine(_reader.ReadLineFrame(), offset) is { } result)
                return result;
        }
        return EndOfFile;
    }

    async ValueTask<CurrentRecordResult> ICurrentRecordSource.ReadCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentPosition = null;
        while (_state != ScanState.Finished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reader.BeginOperation(_pending?.Capture);
            long offset = _reader.Position;
            CapturedRecord? capture;
            try
            {
                capture = await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A partially consumed line cannot be replayed on a forward-only stream.
                Finish();
                _reader.Dispose();
                throw;
            }
            if (ProcessLine(capture, offset) is { } result)
                return result;
        }
        return EndOfFile;
    }

    private CurrentRecordResult? ProcessLine(CapturedRecord? capture, long offset)
    {
        if (capture is not { } frame)
            return CompleteInput();

        var line = new Line(frame, ++_physicalLine, offset);
        var text = frame.Memory.Span.TrimStart();
        return _state switch
        {
            ScanState.FindMarker => FindMarker(text),
            ScanState.FindMarkedHeader => FindMarkedHeader(line, text),
            ScanState.ReadRows => ReadTableLine(line, text),
            _ => FindTable(line, text)
        };
    }

    private CurrentRecordResult? CompleteInput()
    {
        var last = _pending;
        bool reading = _state == ScanState.ReadRows;
        var end = Finish();
        return reading && last is { } row ? PublishRow(row) : end;
    }

    private CurrentRecordResult? FindMarker(ReadOnlySpan<char> text)
    {
        if (text.TrimEnd().SequenceEqual(Profile.TextMarker.AsSpan()))
            _state = ScanState.FindMarkedHeader;
        return null;
    }

    private CurrentRecordResult? FindMarkedHeader(Line line, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return null;
        if (text is not ['|', ..]) return Finish();
        _pending = line;
        _state = ScanState.FindMarkedDelimiter;
        return null;
    }

    private CurrentRecordResult? FindTable(Line line, ReadOnlySpan<char> text)
    {
        var previous = _pending;
        _pending = text is ['|', ..] ? line : null;
        if (_pending is not null && previous is { } header && TryGetTableWidth(header, text, out int width))
        {
            _pending = null;
            if (_tablesToSkip > 0)
                _tablesToSkip--;
            else
                SelectTable(header, width);
            return null;
        }
        return _state == ScanState.FindMarkedDelimiter ? Finish() : null;
    }

    private CurrentRecordResult? ReadTableLine(Line line, ReadOnlySpan<char> text)
    {
        var previous = _pending;
        _pending = text is ['|', ..] ? line : null;
        if (_pending is not null && previous is { } header && TryGetTableWidth(header, text, out _))
            return Finish();

        if (text.IsEmpty)
            _state = ScanState.Finished;
        return previous is { } bodyRow ? PublishRow(bodyRow) : null;
    }

    private static bool TryGetTableWidth(Line header, ReadOnlySpan<char> delimiter, out int width)
    {
        width = MarkdownTableRow.CountDelimiterCells(delimiter);
        return width != 0 && width == MarkdownTableRow.CountCells(header.Capture.Memory.Span);
    }

    private void SelectTable(Line header, int width)
    {
        var names = new string[width];
        var cells = new MarkdownTableRow.Cells(header.Capture.Memory.Span);
        for (int i = 0; i < width && cells.TryRead(out var cell); i++)
        {
            var value = header.Capture.Memory.Slice(cell.Value.Start, cell.Value.Length);
            names[i] = cell.Value.IsEscaped ? MarkdownTableRow.DecodePipeEscapes(value) : value.ToString();
        }
        Header = names;
        _fields = new FieldSpan[width];
        _state = ScanState.ReadRows;
    }

    private CurrentRecordResult? PublishRow(Line row)
    {
        long recordNumber = ++_recordNumber;
        var memory = row.Capture.Memory;
        var cells = new MarkdownTableRow.Cells(memory.Span);
        int ordinal = 0;
        while (ordinal < _fields.Length && cells.TryRead(out var cell))
            _fields[ordinal++] = cell;
        _fields.AsSpan(ordinal).Clear();

        var position = new RecordPosition(recordNumber, row.Number, row.Offset, PositionOffsetUnit.Utf16CodeUnits);
        if (cells.TryRead(out var surplus))
        {
            var action = HandleSurplusCell(memory.Span, surplus.Value, position);
            if (action == BadDataAction.SkipRecord) return null;
            if (action == BadDataAction.Stop) return Finish();
        }

        CurrentPosition = Profile.TrackPosition ? position : null;
        return new(false, new CurrentRecord(memory, _fields), RecordState.Record);
    }

    private BadDataAction HandleSurplusCell(ReadOnlySpan<char> line, SpanInfo surplus, RecordPosition position)
    {
        var error = new InvalidDataException(
            $"Markdown table row at line {position.PhysicalLine} has more than {_fields.Length} cells.");
        if (Profile.BadDataPolicy is not { } policy)
            throw error;
        if (++_errorCount > policy.MaximumErrors)
            throw new InvalidDataException($"The maximum bad-data count of {policy.MaximumErrors} was exceeded.", error);

        var action = policy.Handler(new BadDataContext(position.RecordNumber, position.PhysicalLine, _fields.Length,
            position.Offset + surplus.Start, ParserState.Error, line.Slice(surplus.Start, surplus.Length).ToString(), error)
        { RecordPosition = Profile.TrackPosition ? position : null });

        return action switch
        {
            BadDataAction.SkipRecord or BadDataAction.Stop or BadDataAction.ReturnPartialRecord => action,
            _ => throw error
        };
    }

    private CurrentRecordResult Finish()
    {
        _state = ScanState.Finished;
        _pending = null;
        return EndOfFile;
    }

    public void Dispose() => _reader.Dispose();

    private readonly record struct Line(CapturedRecord Capture, long Number, long Offset);

    private enum ScanState
    {
        FindTable,
        FindMarker,
        FindMarkedHeader,
        FindMarkedDelimiter,
        ReadRows,
        Finished
    }
}
