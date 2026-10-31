namespace PocketCsvReader.WebLogs;

internal sealed class WebLogRecordSource : IRecordSource<WebLogProfile>, IPositionSource
{
    internal static readonly string[] CommonFields =
    [
        "RemoteHost",
        "Identity",
        "AuthenticatedUser",
        "Timestamp",
        "Request",
        "StatusCode",
        "ResponseBytes"
    ];

    private readonly BufferedRecordReader _reader;
    private readonly PositionTracker? _position;
    PositionTracker? IPositionSource.Tracker => _position;
    public RecordPosition? CurrentPosition => _position?.CurrentPosition;
    private readonly List<W3cDirective> _directives = [];
    // Character memory and field metadata belong to the current record until the next read.
    private FieldSpan[] _fields = [];
    private int _lineNumber;
    private bool _completed;

    public WebLogProfile Profile { get; }
    public string[]? Fields { get; private set; }
    public IReadOnlyList<W3cDirective> Directives => _directives;

    public WebLogRecordSource(StreamReader reader, WebLogProfile profile)
    {
        if (profile.TrackPosition) _position = new();
        _reader = new BufferedRecordReader(reader, profile.ParserOptimizations.BufferSize, tracker: _position);
        Profile = profile;
        if (profile.Format == WebLogFormat.Common)
        {
            Fields = [.. CommonFields];
            _fields = new FieldSpan[CommonFields.Length];
        }
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (_completed)
        {
            record = new RecordSpan([], []);
            recordState = RecordState.Eof;
            return true;
        }

        while (true)
        {
            _reader.BeginOperation();
            _position?.BeginRecord();
            var line = _reader.ReadLineFrame();
            if (line is not { } captured)
            {
                _completed = true;
                record = new RecordSpan([], []);
                recordState = RecordState.Eof;
                return true;
            }

            if (!ParseLine(captured.Memory.Span))
                continue;

            record = RecordSpan.FromMemory(captured.Memory, _fields);
            recordState = RecordState.Record;
            return false;
        }
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        while (true)
        {
            _reader.BeginOperation();
            _position?.BeginRecord();
            var line = await _reader.ReadLineFrameAsync(cancellationToken).ConfigureAwait(false);
            if (line is not { } captured)
            {
                _completed = true;
                return new(true, RecordMemory.Empty, RecordState.Eof);
            }

            if (!ParseLine(captured.Memory.Span))
                continue;

            return new(false, new RecordMemory(captured.Memory, _fields), RecordState.Record);
        }
    }

    private bool ParseLine(ReadOnlySpan<char> line)
    {
        _lineNumber++;
        if (line.IsWhiteSpace())
            return false;
        if (Profile.Format == WebLogFormat.W3cExtended && line[0] == '#')
        {
            ParseDirective(line);
            return false;
        }

        _position?.CompleteRecord();
        if (Profile.Format == WebLogFormat.Common)
            ParseCommon(line);
        else
            ParseW3c(line);
        return true;
    }

    private void ParseDirective(ReadOnlySpan<char> line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 1)
            return;

        // Directive strings are retained public metadata, rather than temporary row storage.
        var name = line[1..separator].Trim().ToString();
        var value = line[(separator + 1)..].Trim().ToString();
        _directives.Add(new W3cDirective(name, value, _lineNumber));

        if (!name.Equals("Fields", StringComparison.OrdinalIgnoreCase))
            return;

        var fields = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
            throw Invalid("the #Fields directive does not define any fields");
        if (Fields is not null && !Fields.SequenceEqual(fields, StringComparer.Ordinal))
            throw Invalid("a #Fields directive changes the schema after it was established");
        Fields = fields;
        if (_fields.Length != fields.Length)
            _fields = new FieldSpan[fields.Length];
    }

    private void ParseW3c(ReadOnlySpan<char> line)
    {
        if (Fields is null)
            throw Invalid("a data record appears before the required #Fields directive");

        var offset = 0;
        var count = 0;
        while (true)
        {
            SkipWhitespace(line, ref offset);
            if (offset == line.Length)
                break;
            var field = ReadToken(line, ref offset);
            if (count < _fields.Length)
                _fields[count] = field;
            count++;
        }
        if (count != Fields.Length)
            throw Invalid($"expected {Fields.Length} fields but found {count}");
    }

    private void ParseCommon(ReadOnlySpan<char> line)
    {
        var offset = 0;

        _fields[0] = ReadToken(line, ref offset);
        _fields[1] = ReadToken(line, ref offset);
        _fields[2] = ReadToken(line, ref offset);
        _fields[3] = ReadDelimited(line, ref offset, '[', ']', 4);
        _fields[4] = ReadDelimited(line, ref offset, '"', '"', 5);
        _fields[5] = ReadToken(line, ref offset);
        _fields[6] = ReadToken(line, ref offset);
        SkipWhitespace(line, ref offset);

        if (offset != line.Length)
            throw Invalid($"expected a {CommonFields.Length}-field Common Log Format record");
    }

    private FieldSpan ReadToken(ReadOnlySpan<char> line, ref int offset)
    {
        SkipWhitespace(line, ref offset);
        var start = offset;
        while (offset < line.Length && !char.IsWhiteSpace(line[offset]))
            offset++;
        if (start == offset)
            throw Invalid("a required field is missing");
        return new FieldSpan(start, offset - start);
    }

    private FieldSpan ReadDelimited(ReadOnlySpan<char> line, ref int offset, char prefix, char suffix, int fieldNumber)
    {
        SkipWhitespace(line, ref offset);
        if (offset >= line.Length || line[offset] != prefix)
            throw Invalid($"expected '{prefix}' at field {fieldNumber}");

        var start = ++offset;
        while (offset < line.Length)
        {
            if (line[offset] == suffix && (offset == start || line[offset - 1] != '\\'))
                break;
            offset++;
        }
        if (offset >= line.Length)
            throw Invalid($"field {fieldNumber} is missing its closing '{suffix}'");

        var field = new FieldSpan(start, offset - start, prefix == '"', false);
        offset++;
        return field;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> line, ref int offset)
    {
        while (offset < line.Length && char.IsWhiteSpace(line[offset]))
            offset++;
    }

    private InvalidDataException Invalid(string reason)
        => new($"Invalid {FormatName()} record at line {_lineNumber}: {reason}.");

    private string FormatName()
        => Profile.Format == WebLogFormat.Common ? "Common Log Format" : "W3C Extended Log Format";

    public void Dispose() => _reader.Dispose();
}
