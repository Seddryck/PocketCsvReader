using PocketCsvReader.Json;

namespace PocketCsvReader.Ndjson;

// NDJSON owns line boundaries; the shared JSON grammar owns validation and offsets.
// Raw line breaks inside strings are invalid JSON, so locating LF in chunks cannot
// split a valid string. CRLF may straddle chunks; the previous character is retained.
internal sealed class ProjectedNdjsonFramer(ProjectedJsonValueFramer valueFramer, bool crlf) : IRecordFramer
{
    private int _total;
    private char _previous;
    private bool _started;
    private bool _complete;
    private char[] _whitespace = new char[16];
    private int _whitespaceLength;
    public int ValueStart { get; private set; }
    public int ValueLength { get; private set; }
    public bool HasValue => _started;

    public void Reset()
    {
        _total = 0;
        _previous = '\0';
        _started = false;
        _complete = false;
        _whitespaceLength = 0;
        ValueStart = 0;
        ValueLength = 0;
        valueFramer.Prepare('{', 0);
        valueFramer.Reset();
    }

    public FrameScanResult Scan(ReadOnlySpan<char> input)
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
                ValidateComplete();
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
            // ParseCandidate trims its input before validation. Defer a chunk's
            // trailing whitespace until another token arrives, preserving EOF
            // diagnostics (including truncated numbers/strings) across refills.
            int end = input.Length;
            while (end > index && IsWhitespace(input[end - 1])) end--;
            if (end > index && _whitespaceLength > 0)
            {
                var whitespaceResult = valueFramer.Scan(_whitespace.AsSpan(0, _whitespaceLength));
                ValueLength += whitespaceResult.Consumed;
                _whitespaceLength = 0;
            }
            var result = valueFramer.Scan(input[index..end]);
            ValueLength += result.Consumed;
            index += result.Consumed;
            _complete = result.Complete;
            if (!_complete)
            {
                int length = input.Length - end;
                if (_whitespaceLength + length > _whitespace.Length)
                    Array.Resize(ref _whitespace, Math.Max(_whitespace.Length * 2, _whitespaceLength + length));
                input[end..].CopyTo(_whitespace.AsSpan(_whitespaceLength));
                _whitespaceLength += length;
            }
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

    private void ValidateComplete()
    {
        if (_started && !_complete) valueFramer.CompleteAtEof();
    }

    public FrameScanResult CompleteAtEof()
    {
        ValidateComplete();
        return _total == 0 ? default : new(0, true, _total);
    }

    private static bool IsWhitespace(char current) => current is ' ' or '\t' or '\r' or '\n';
}
