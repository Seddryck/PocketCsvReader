using PocketCsvReader.Json;

namespace PocketCsvReader.Ndjson;

// NDJSON owns line boundaries; the shared JSON grammar owns validation and offsets.
// Failed validation switches to lexical framing, then the captured record is parsed
// by the compatible path. Errors are reported only after the capture has ended.
internal sealed class ProjectedNdjsonFramer(
    ProjectedJsonValueFramer valueFramer,
    NdjsonRecordSource.NdjsonFramer fallback,
    bool crlf) : IRecordFramer
{
    private int _total;
    private char _previous;
    private bool _started;
    private bool _complete;
    public int ValueStart { get; private set; }
    public int ValueLength { get; private set; }
    public bool HasValue => _started;
    public bool RequiresFallback { get; private set; }

    public void Reset()
    {
        _total = 0;
        _previous = '\0';
        _started = false;
        _complete = false;
        RequiresFallback = false;
        ValueStart = 0;
        ValueLength = 0;
        valueFramer.Prepare('{', 0);
        valueFramer.Reset();
    }

    public FrameScanResult Scan(ReadOnlySpan<char> input)
    {
        if (RequiresFallback) return fallback.Scan(input);

        // Snapshot only lexical state at this chunk's start. On error, replay
        // this chunk (not the record) through the existing NDJSON boundary rules.
        int total = _total;
        bool inString = valueFramer.IsInString;
        bool escaping = valueFramer.IsStringEscapePending;
        int matched = crlf && !inString && _previous == '\r' ? 1 : 0;
        try
        {
            return ScanProjected(input);
        }
        catch (InvalidDataException)
        {
            RequiresFallback = true;
            fallback.Resume(total, inString, escaping, matched);
            return fallback.Scan(input);
        }
    }

    private FrameScanResult ScanProjected(ReadOnlySpan<char> input)
    {
        int consumed = 0;
        while (consumed < input.Length)
        {
            var remaining = input[consumed..];
            int newline = remaining.IndexOf('\n');
            int length = newline < 0 ? remaining.Length : newline;
            Parse(remaining[..length]);
            bool boundary = newline >= 0 && !valueFramer.IsInString
                && (!crlf || (newline == 0 ? _previous : remaining[newline - 1]) == '\r');
            _total += length;
            consumed += length;
            if (boundary)
            {
                // Incomplete objects must also finish their capture before the
                // compatible parser supplies its trimmed-input diagnostic.
                RequiresFallback = _started && !_complete;
                _total++;
                return new(consumed + 1, true, _total - (crlf ? 2 : 1));
            }
            if (newline >= 0)
            {
                Parse(remaining.Slice(length, 1));
                _total++;
                consumed++;
                _previous = '\n';
            }
            else if (length > 0)
            {
                _previous = remaining[length - 1];
            }
        }
        return new(input.Length, false);
    }

    private void Parse(ReadOnlySpan<char> input)
    {
        int index = 0;
        if (!_started)
        {
            while (index < input.Length && IsWhitespace(input[index])) index++;
            if (index == input.Length) return;
            ValueStart = _total + index;
            _started = true;
        }
        if (!_complete)
        {
            var result = valueFramer.Scan(input[index..]);
            ValueLength += result.Consumed;
            index += result.Consumed;
            _complete = result.Complete;
        }
        if (_complete)
        {
            foreach (char current in input[index..])
            {
                if (!IsWhitespace(current))
                    throw new InvalidDataException($"Unexpected character after the JSON object at position {ValueLength}.");
            }
        }
    }

    public FrameScanResult CompleteAtEof()
    {
        if (RequiresFallback) return fallback.CompleteAtEof();
        RequiresFallback = _started && !_complete;
        return _total == 0 ? default : new(0, true, _total);
    }

    private static bool IsWhitespace(char current) => current is ' ' or '\t' or '\r' or '\n';
}
