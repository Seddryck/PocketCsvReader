using PocketCsvReader.KeyValue.Configuration;
using System.Runtime.InteropServices;

namespace PocketCsvReader.KeyValue;

internal sealed class KeyValueRecordSource : IRecordSource<KeyValueProfile>, IPositionSource
{
    private readonly BufferedRecordReader _reader;
    private readonly PositionTracker? _position;
    private readonly List<FieldSpan> _scratchFields = [];
    private readonly FieldMetadataBuffer _metadata = new();
    // Borrowed character and metadata storage is valid until the next read.
    private FieldSpan[] _fields = [];
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;

    public KeyValueProfile Profile { get; }

    public KeyValueRecordSource(StreamReader reader, KeyValueProfile profile)
    {
        Profile = profile;
        if (profile.TrackPosition) _position = new();
        _reader = new(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        BeginRead();
        var line = _reader.ReadLineFrame();
        if (line is not { } captured)
        {
            record = RecordSpan.FromMemory(ReadOnlyMemory<char>.Empty, []);
            recordState = RecordState.Eof;
            return true;
        }

        Parse(captured.Memory.Span);
        record = RecordSpan.FromMemory(captured.Memory, _fields);
        recordState = RecordState.Record;
        // Discover EOF on the next read; lookahead could refill and invalidate this view.
        return false;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginRead();
        var line = await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
        if (line is not { } captured)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        Parse(captured.Memory.Span);
        return new(false, new RecordMemory(captured.Memory, _fields), RecordState.Record);
    }

    private void BeginRead()
    {
        _reader.BeginOperation();
        _position?.BeginRecord();
    }

    private void Parse(ReadOnlySpan<char> line)
    {
        _position?.CompleteRecord();
        KeyValueRecordParser.Parse(line, Profile.Format, _scratchFields);
        _fields = _metadata.PublishBorrowed(CollectionsMarshal.AsSpan(_scratchFields));
        // Do not retain decoded strings in both temporary and returned storage.
        _scratchFields.Clear();
    }

    public void Dispose() => _reader.Dispose();
}
