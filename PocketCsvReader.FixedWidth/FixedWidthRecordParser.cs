using System.Buffers;
using System.Text;
using PocketCsvReader;

namespace PocketCsvReader.FixedWidth;

internal sealed class FixedWidthRecordParser : IRecordSource<FixedWidthProfile>, IPositionSource
{
    private readonly StreamReader _reader;
    private readonly PositionTracker? _position;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    private readonly char[] _buffer;
    private readonly StringBuilder _record = new();
    private readonly FieldSpan[] _fieldSpans;
    private int _bufferOffset;
    private int _bufferLength;
    private int _recordNumber;
    private bool _completed;
    private bool _disposed;

    public FixedWidthProfile Profile { get; }

    public FixedWidthRecordParser(StreamReader reader, FixedWidthProfile profile)
    {
        _reader = reader;
        Profile = profile;
        if (profile.TrackPosition) _position = new();
        _buffer = ArrayPool<char>.Shared.Rent(profile.ParserOptimizations.BufferSize);
        _fieldSpans = profile.Descriptor.Fields
            .Select(field => new FieldSpan(field.Offset, field.Width))
            .ToArray();
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (_completed)
        {
            record = new RecordSpan([], []);
            recordState = RecordState.Eof;
            return true;
        }

        _position?.BeginRecord();
        _record.Clear();
        while (true)
        {
            if (_bufferOffset == _bufferLength)
            {
                _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
                _bufferOffset = 0;
                if (_bufferLength == 0)
                {
                    _completed = true;
                    if (_record.Length == 0)
                    {
                        record = new RecordSpan([], []);
                        recordState = RecordState.Eof;
                        return true;
                    }

                    var finalRecord = _record.ToString();
                    ValidateRecord(finalRecord.Length);
                    record = new RecordSpan(finalRecord.AsSpan(), _fieldSpans);
                    recordState = RecordState.Record;
                    return true;
                }
            }

            char current = _buffer[_bufferOffset++];
            _position?.Advance(current);
            _record.Append(current);
            if (!EndsWithLineTerminator())
                continue;

            _record.Length -= Profile.Descriptor.LineTerminator.Length;
            var value = _record.ToString();
            ValidateRecord(value.Length);
            record = new RecordSpan(value.AsSpan(), _fieldSpans);
            recordState = RecordState.Record;
            return false;
        }
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        _position?.BeginRecord();
        _record.Clear();
        while (true)
        {
            if (_bufferOffset == _bufferLength)
            {
                _bufferLength = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferOffset = 0;
                if (_bufferLength == 0)
                {
                    _completed = true;
                    if (_record.Length == 0)
                        return new(true, RecordMemory.Empty, RecordState.Eof);

                    var finalRecord = _record.ToString();
                    ValidateRecord(finalRecord.Length);
                    return new(true, new RecordMemory(finalRecord.AsSpan(), _fieldSpans), RecordState.Record);
                }
            }

            char current = _buffer[_bufferOffset++];
            _position?.Advance(current);
            _record.Append(current);
            if (!EndsWithLineTerminator())
                continue;

            _record.Length -= Profile.Descriptor.LineTerminator.Length;
            var value = _record.ToString();
            ValidateRecord(value.Length);
            return new(false, new RecordMemory(value.AsSpan(), _fieldSpans), RecordState.Record);
        }
    }

    private bool EndsWithLineTerminator()
    {
        var terminator = Profile.Descriptor.LineTerminator;
        if (_record.Length < terminator.Length)
            return false;
        var start = _record.Length - terminator.Length;
        for (var i = 0; i < terminator.Length; i++)
            if (_record[start + i] != terminator[i])
                return false;
        return true;
    }

    private void ValidateRecord(int actualWidth)
    {
        _recordNumber++;
        _position?.CompleteRecord();
        var expectedWidth = Profile.RecordWidth;
        if (actualWidth < expectedWidth)
            throw new InvalidDataException($"Fixed-width record {_recordNumber} is too short: expected {expectedWidth} characters but found {actualWidth}.");
        if (!Profile.Descriptor.AllowTrailingCharacters && actualWidth > expectedWidth)
            throw new InvalidDataException($"Fixed-width record {_recordNumber} is too long: expected {expectedWidth} characters but found {actualWidth}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        ArrayPool<char>.Shared.Return(_buffer);
        _disposed = true;
    }
}
