using System.Buffers;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;

// Only ASCII syntax is supported. Non-ASCII payload bytes are opaque to the
// shared state machine; all offsets are byte offsets until a field is requested.
internal sealed class Utf8RecordSource : IRecordSource<CsvProfile>
{
    public CsvProfile Profile { get; }
    private readonly Stream _stream;
    private readonly IParser _parser;
    private readonly byte[] _input;
    private byte[] _record;
    private readonly int _readSize;
    private int _position, _available;
    private bool _disposed;
    private readonly List<FieldSpan> _fields = new();
    private int _length;

    public Utf8RecordSource(Stream stream, CsvProfile profile)
    {
        Profile = profile;
        _stream = stream;
        _parser = FieldParserFactory.Create(profile.Dialect);
        _readSize = profile.ParserOptimizations.BufferSize;
        if (_readSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile), "Buffer size must be positive.");
        _input = ArrayPool<byte>.Shared.Rent(_readSize);
        _record = ArrayPool<byte>.Shared.Rent(_readSize);
    }

    public static bool Supports(CsvProfile profile)
    {
        var d = profile.Dialect;
        return d.Delimiter < 128 && (d.QuoteChar is null or < (char)128)
            && (d.EscapeChar is null or < (char)128) && (d.CommentChar is null or < (char)128)
            && d.LineTerminator.Length > 0 && d.LineTerminator.All(c => c < 128)
            && d.ArrayDelimiter is null && d.ArrayPrefix is null && d.ArraySuffix is null
            && profile.BadDataPolicy is null;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RecordReadResult result;
        while (!TryRead(false, out result))
        {
            _available = _stream.Read(_input, 0, _readSize);
            _position = 0;
            if (_available == 0)
            {
                TryRead(true, out result);
                break;
            }
        }
        record = result.Record.AsRecordSpan();
        recordState = result.State;
        return result.IsEndOfFile;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        RecordReadResult result;
        while (!TryRead(false, out result))
        {
            _available = await _stream.ReadAsync(_input.AsMemory(0, _readSize), cancellationToken).ConfigureAwait(false);
            _position = 0;
            if (_available == 0)
            {
                TryRead(true, out result);
                return result;
            }
        }
        return result;
    }

    private bool TryRead(bool eof, out RecordReadResult result)
    {
        var fields = _fields;
        int length = _length;
        while (true)
        {
            if (_position == _available)
            {
                if (!eof)
                {
                    _length = length;
                    result = default;
                    return false;
                }
                else
                {
                    if (length == 0)
                    {
                        result = new(true, RecordMemory.Empty, RecordState.Eof);
                        return true;
                    }
                    var end = _parser.ParseEof(length);
                    if (end is ParserState.Record or ParserState.Header)
                        fields.Add(_parser.Result);
                    else if (end != ParserState.Eof)
                        throw new InvalidDataException("Invalid character End-of-File.");
                    result = new(true, new RecordMemory(_record.AsMemory(0, length), fields.ToArray()),
                        end == ParserState.Eof ? RecordState.Eof : RecordState.Record);
                    _parser.Reset(true);
                    _length = 0;
                    fields.Clear();
                    return true;
                }
            }

            byte value = _input[_position];
            // Map payload bytes outside ASCII to one opaque symbol. No decoding
            // or UTF-16 buffer is needed to recognize CSV syntax.
            var state = _parser.Parse(value < 128 ? (char)value : '\uffff', length);
            if (state == ParserState.Reprocess)
                continue;
            if (state == ParserState.Error)
                throw new InvalidDataException($"Invalid byte '{value}' at position {length}.");
            if (length == _record.Length)
            {
                var larger = ArrayPool<byte>.Shared.Rent(checked(length * 2));
                _record.CopyTo(larger, 0);
                ArrayPool<byte>.Shared.Return(_record);
                _record = larger;
            }
            _record[length++] = value;
            _position++;
            if (state is ParserState.Field or ParserState.Record or ParserState.Header)
            {
                fields.Add(_parser.Result);
                _parser.Reset(state != ParserState.Field);
            }
            if (state is ParserState.Record or ParserState.Header or ParserState.Comment)
            {
                result = new(false, state == ParserState.Comment ? RecordMemory.Empty
                    : new RecordMemory(_record.AsMemory(0, length), fields.ToArray()),
                    state == ParserState.Comment ? RecordState.Comment : RecordState.Record);
                _parser.Reset(true);
                _length = 0;
                fields.Clear();
                return true;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_input);
        ArrayPool<byte>.Shared.Return(_record);
        _stream.Dispose();
    }
}
