namespace PocketCsvReader.SecurityLogs;

/// <summary>Owns buffered input and reusable metadata; views expire on the next read or disposal.</summary>
internal sealed class SecurityLogRecordSource : IRecordSource<SecurityLogProfile>, IPositionSource
{
    private readonly BufferedRecordReader _reader;
    private readonly PositionTracker _position = new();
    private readonly SecurityLogParser _parser = new();
    private FieldSpan[] _fields = [];
    public string[]? Fields { get; private set; }
    public SecurityLogProfile Profile { get; }
    PositionTracker? IPositionSource.Tracker => Profile.TrackPosition ? _position : null;
    public RecordPosition? CurrentPosition => Profile.TrackPosition ? _position.CurrentPosition : null;

    public SecurityLogRecordSource(StreamReader reader, SecurityLogProfile profile)
    {
        Profile = profile;
        // Tracking also supplies absolute line/offset diagnostics when public tracking is disabled.
        _reader = new(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
        if (profile.Attributes is not null)
            SetFields([.. SecurityLogParser.HeaderNames(profile.Format), .. profile.Attributes]);
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        BeginRead();
        var capture = _reader.ReadLineFrame();
        if (capture is null)
        {
            record = RecordSpan.FromMemory(ReadOnlyMemory<char>.Empty, []);
            recordState = RecordState.Eof;
            return true;
        }
        record = Parse(capture.Value.Memory);
        recordState = RecordState.Record;
        return false;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginRead();
        var capture = await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
        return capture is null ? new(true, RecordMemory.Empty, RecordState.Eof)
            : new(false, Parse(capture.Value.Memory).AsMemory(), RecordState.Record);
    }

    private void BeginRead()
    {
        _reader.BeginOperation();
        _position.BeginRecord();
    }

    private RecordSpan Parse(ReadOnlyMemory<char> memory)
    {
        try
        {
            _parser.Parse(memory.Span, Profile.Format);
            if (Fields is null)
            {
                var names = new string[_parser.Fields.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    var field = _parser.Fields[i];
                    names[i] = field.DecodedLabel ?? memory.Span.Slice(field.Label.Start, field.Label.Length).ToString();
                }
                SetFields(names);
            }
            // A stable output shape: absent keys are database null, unselected keys are validated then ignored.
            for (int i = 0; i < _fields.Length; i++)
                _fields[i] = new FieldSpan(new SpanInfo(0, 0, IsNull: true), default);
            foreach (var field in _parser.Fields)
            {
                var label = field.DecodedLabel is { } name ? name.AsSpan() : memory.Span.Slice(field.Label.Start, field.Label.Length);
                for (int i = 0; i < Fields!.Length; i++)
                    if (label.SequenceEqual(Fields[i])) { _fields[i] = field; break; }
            }
            _position.CompleteRecord();
            return RecordSpan.FromMemory(memory, _fields);
        }
        catch (InvalidDataException exception)
        {
            var position = _position.PendingPosition;
            throw new InvalidDataException($"{Profile.Format} record {position.RecordNumber}, line {position.PhysicalLine}, offset {position.Offset}: {exception.Message}", exception);
        }
        finally { _parser.Fields.Clear(); }
    }

    private void SetFields(string[] names)
    {
        Fields = names;
        _fields = new FieldSpan[names.Length];
    }

    public void Dispose() => _reader.Dispose();
}
