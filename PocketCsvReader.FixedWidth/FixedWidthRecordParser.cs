using System.Buffers;
using PocketCsvReader;

namespace PocketCsvReader.FixedWidth;

// Returned views borrow the input buffer or spill storage until the next read/disposal.
internal sealed class FixedWidthRecordParser : IRecordSource<FixedWidthProfile>, IPositionSource
{
    private readonly StreamReader _reader;
    private readonly PositionTracker? _position;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    private readonly char[] _buffer;
    private char[]? _spill;
    private readonly FieldSpan[] _fieldSpans;
    private readonly int[] _terminatorFallback;
    private int _bufferOffset;
    private int _bufferLength;
    private int _spillLength;
    private long _recordLength;
    private int _matchedTerminator;
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
        // Prefix fallback preserves overlapping custom terminators across input buffers.
        var terminator = profile.Descriptor.LineTerminator;
        _terminatorFallback = new int[terminator.Length];
        for (int i = 1, matched = 0; i < terminator.Length; i++)
        {
            while (matched > 0 && terminator[i] != terminator[matched])
                matched = _terminatorFallback[matched - 1];
            if (terminator[i] == terminator[matched]) matched++;
            _terminatorFallback[i] = matched;
        }
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        BeginRead();
        ReadOnlyMemory<char> memory = default;
        while (!_completed && !TryFrame(out memory))
        {
            _bufferLength = _reader.Read(_buffer, 0, _buffer.Length);
            _bufferOffset = 0;
            if (_bufferLength == 0) memory = FinishInput();
        }
        recordState = memory.IsEmpty ? RecordState.Eof : RecordState.Record;
        record = RecordSpan.FromMemory(memory, memory.IsEmpty ? [] : _fieldSpans);
        return _completed;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginRead();
        ReadOnlyMemory<char> memory = default;
        while (!_completed && !TryFrame(out memory))
        {
            _bufferLength = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _bufferOffset = 0;
            if (_bufferLength == 0) memory = FinishInput();
        }
        return memory.IsEmpty
            ? new(true, RecordMemory.Empty, RecordState.Eof)
            : new(_completed, new RecordMemory(memory, _fieldSpans), RecordState.Record);
    }

    private void BeginRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_completed) _position?.BeginRecord();
        _spillLength = 0;
        _recordLength = 0;
        _matchedTerminator = 0;
    }

    // Shared framing/validation for synchronous and asynchronous refills.
    private bool TryFrame(out ReadOnlyMemory<char> memory)
    {
        var start = _bufferOffset;
        var terminator = Profile.Descriptor.LineTerminator;
        while (_bufferOffset < _bufferLength)
        {
            var current = _buffer[_bufferOffset++];
            while (_matchedTerminator > 0 && current != terminator[_matchedTerminator])
                _matchedTerminator = _terminatorFallback[_matchedTerminator - 1];
            if (current == terminator[_matchedTerminator]) _matchedTerminator++;
            if (_matchedTerminator != terminator.Length) continue;

            var length = _bufferOffset - start;
            _position?.Advance(_buffer.AsSpan(start, length));
            var width = _recordLength + length - terminator.Length;
            ValidateRecord(width);
            if (_recordLength == 0)
                memory = _buffer.AsMemory(start, (int)width);
            else
            {
                PreserveFields(_buffer.AsSpan(start, length));
                memory = _spill.AsMemory(0, Profile.RecordWidth);
            }
            return true;
        }

        var consumed = _bufferOffset - start;
        _position?.Advance(_buffer.AsSpan(start, consumed));
        if (consumed > 0)
        {
            PreserveFields(_buffer.AsSpan(start, consumed));
            _recordLength += consumed;
        }
        memory = default;
        return false;
    }

    private void PreserveFields(ReadOnlySpan<char> input)
    {
        // Trailing characters and terminators need counting/matching, not storage.
        // Bound spill memory by the layout width, even for arbitrarily long invalid tails.
        var count = Math.Min(input.Length, Profile.RecordWidth - _spillLength);
        if (count == 0) return;
        _spill ??= ArrayPool<char>.Shared.Rent(Profile.RecordWidth);
        input[..count].CopyTo(_spill.AsSpan(_spillLength));
        _spillLength += count;
    }

    private ReadOnlyMemory<char> FinishInput()
    {
        _completed = true;
        if (_recordLength == 0) return default;
        ValidateRecord(_recordLength);
        return _spill.AsMemory(0, Profile.RecordWidth);
    }

    private void ValidateRecord(long actualWidth)
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
        if (_disposed) return;
        ArrayPool<char>.Shared.Return(_buffer);
        if (_spill is not null) ArrayPool<char>.Shared.Return(_spill);
        _disposed = true;
    }
}
