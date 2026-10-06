namespace PocketCsvReader.WebLogs;

internal sealed class WebLogRecordSource : IRecordSource<WebLogProfile>
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

    private readonly StreamReader _reader;
    private readonly List<W3cDirective> _directives = [];
    private int _lineNumber;
    private bool _completed;

    public WebLogProfile Profile { get; }
    public string[]? Fields { get; private set; }
    public IReadOnlyList<W3cDirective> Directives => _directives;

    public WebLogRecordSource(StreamReader reader, WebLogProfile profile)
    {
        _reader = reader;
        Profile = profile;
        if (profile.Format == WebLogFormat.Common)
            Fields = [.. CommonFields];
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
            var line = _reader.ReadLine();
            if (line is null)
            {
                _completed = true;
                record = new RecordSpan([], []);
                recordState = RecordState.Eof;
                return true;
            }

            _lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (Profile.Format == WebLogFormat.W3cExtended && line.StartsWith('#'))
            {
                ParseDirective(line);
                continue;
            }

            var spans = Profile.Format == WebLogFormat.Common
                ? ParseCommon(line)
                : ParseW3c(line);
            record = new RecordSpan(line, spans);
            recordState = RecordState.Record;
            return false;
        }
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                _completed = true;
                return new(true, RecordMemory.Empty, RecordState.Eof);
            }

            _lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (Profile.Format == WebLogFormat.W3cExtended && line.StartsWith('#'))
            {
                ParseDirective(line);
                continue;
            }

            var spans = Profile.Format == WebLogFormat.Common
                ? ParseCommon(line)
                : ParseW3c(line);
            return new(false, new RecordMemory(line, spans), RecordState.Record);
        }
    }

    private void ParseDirective(string line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 1)
            return;

        var name = line[1..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        _directives.Add(new W3cDirective(name, value, _lineNumber));

        if (!name.Equals("Fields", StringComparison.OrdinalIgnoreCase))
            return;

        var fields = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
            throw Invalid("the #Fields directive does not define any fields");
        if (Fields is not null && !Fields.SequenceEqual(fields, StringComparer.Ordinal))
            throw Invalid("a #Fields directive changes the schema after it was established");
        Fields = fields;
    }

    private FieldSpan[] ParseW3c(string line)
    {
        if (Fields is null)
            throw Invalid("a data record appears before the required #Fields directive");

        var spans = ParseWhitespaceSeparated(line);
        if (spans.Length != Fields.Length)
            throw Invalid($"expected {Fields.Length} fields but found {spans.Length}");
        return spans;
    }

    private FieldSpan[] ParseCommon(string line)
    {
        var spans = new List<FieldSpan>(CommonFields.Length);
        var offset = 0;

        AddToken(line, ref offset, spans);
        AddToken(line, ref offset, spans);
        AddToken(line, ref offset, spans);
        AddDelimited(line, ref offset, '[', ']', spans);
        AddDelimited(line, ref offset, '"', '"', spans);
        AddToken(line, ref offset, spans);
        AddToken(line, ref offset, spans);
        SkipWhitespace(line, ref offset);

        if (spans.Count != CommonFields.Length || offset != line.Length)
            throw Invalid($"expected a {CommonFields.Length}-field Common Log Format record");
        return spans.ToArray();
    }

    private static FieldSpan[] ParseWhitespaceSeparated(string line)
    {
        var spans = new List<FieldSpan>();
        var offset = 0;
        while (true)
        {
            SkipWhitespace(line, ref offset);
            if (offset >= line.Length)
                return spans.ToArray();
            var start = offset;
            while (offset < line.Length && !char.IsWhiteSpace(line[offset]))
                offset++;
            spans.Add(new FieldSpan(start, offset - start));
        }
    }

    private void AddToken(string line, ref int offset, ICollection<FieldSpan> spans)
    {
        SkipWhitespace(line, ref offset);
        var start = offset;
        while (offset < line.Length && !char.IsWhiteSpace(line[offset]))
            offset++;
        if (start == offset)
            throw Invalid("a required field is missing");
        spans.Add(new FieldSpan(start, offset - start));
    }

    private void AddDelimited(string line, ref int offset, char prefix, char suffix, ICollection<FieldSpan> spans)
    {
        SkipWhitespace(line, ref offset);
        if (offset >= line.Length || line[offset] != prefix)
            throw Invalid($"expected '{prefix}' at field {spans.Count + 1}");

        var start = ++offset;
        while (offset < line.Length)
        {
            if (line[offset] == suffix && (offset == start || line[offset - 1] != '\\'))
                break;
            offset++;
        }
        if (offset >= line.Length)
            throw Invalid($"field {spans.Count + 1} is missing its closing '{suffix}'");

        spans.Add(new FieldSpan(start, offset - start, prefix == '"', false));
        offset++;
    }

    private static void SkipWhitespace(string line, ref int offset)
    {
        while (offset < line.Length && char.IsWhiteSpace(line[offset]))
            offset++;
    }

    private InvalidDataException Invalid(string reason)
        => new($"Invalid {FormatName()} record at line {_lineNumber}: {reason}.");

    private string FormatName()
        => Profile.Format == WebLogFormat.Common ? "Common Log Format" : "W3C Extended Log Format";

    public void Dispose()
    { }
}
