using PocketCsvReader.KeyValue.Configuration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PocketCsvReader.KeyValue;

internal sealed class KeyValueRecordSource : IBorrowedRecordSource<KeyValueProfile>, IPositionSource
{
    private readonly BufferedRecordReader _reader;
    private readonly PositionTracker? _position;
    private readonly List<FieldSpan> _scratchFields = [];
    private readonly FieldMetadataBuffer _metadata = new();
    private readonly LabeledColumnProjection? _projection;
    private int? _stableFieldCount;
    // Borrowed character and metadata storage is valid until the next read.
    private FieldSpan[] _fields = [];
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;

    public KeyValueProfile Profile { get; }

    public KeyValueRecordSource(StreamReader reader, KeyValueProfile profile)
    {
        Profile = profile;
        if (profile.Projection is { } columns) _projection = new(columns);
        _position = PositionTracker.ForTextReader(reader, profile.TrackPosition);
        _reader = new(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
    }

    bool IBorrowedRecordSource<KeyValueProfile>.CopyPublicMetadata => true;

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
        => ReadCurrent().PublishOwned(out record, out recordState);

    CurrentRecordResult ICurrentRecordSource.ReadCurrent() => ReadCurrent();

    private CurrentRecordResult ReadCurrent()
    {
        BeginRead();
        return CompleteRead(_reader.ReadLineFrame());
    }

    async ValueTask<CurrentRecordResult> ICurrentRecordSource.ReadCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginRead();
        return CompleteRead(await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CurrentRecordResult CompleteRead(CapturedRecord? line)
    {
        if (line is not { } captured)
            return new(true, default, RecordState.Eof);

        Parse(captured.Memory.Span);
        // Discover EOF on the next read; lookahead could refill and invalidate this view.
        return new(false, new CurrentRecord(captured.Memory, _fields), RecordState.Record);
    }

    private void BeginRead()
    {
        _reader.BeginOperation();
        _position?.BeginRecord();
    }

    private void Parse(ReadOnlySpan<char> line)
    {
        _position?.CompleteRecord();
        var captureLabels = !_stableFieldCount.HasValue;
        KeyValueRecordParser.Parse(line, Profile.Format, _scratchFields, captureLabels);
        var fields = CollectionsMarshal.AsSpan(_scratchFields);
        if (Profile.StableFieldLayout)
        {
            _stableFieldCount ??= fields.Length;
            if (fields.Length != _stableFieldCount.Value)
                throw new InvalidDataException($"Stable field count changed from {_stableFieldCount.Value} to {fields.Length}.");
        }
        if (_projection is null)
        {
            _fields = _metadata.PublishBorrowed(fields);
        }
        else
        {
            if (captureLabels)
                _projection.Resolve(line, fields);
            _fields = _projection.Apply(fields);
        }
        // Do not retain decoded strings in both temporary and returned storage.
        _scratchFields.Clear();
    }

    public void Dispose() => _reader.Dispose();
}
