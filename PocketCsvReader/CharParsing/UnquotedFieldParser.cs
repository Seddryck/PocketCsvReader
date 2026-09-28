namespace PocketCsvReader.CharParsing;

/// <summary>
/// Parses dialects without a quote character without carrying quoted-field states
/// through the per-character hot path.
/// </summary>
internal sealed class UnquotedFieldParser : IParser
{
    private readonly FieldContext _context = new();
    private readonly char _delimiter;
    private readonly string _lineTerminator;
    private readonly char? _escape;
    private readonly char? _comment;
    private readonly bool _skipInitialSpace;

    private bool _recordStart = true;
    private bool _inComment;
    private int _terminatorIndex;
    private int _terminatorStart;

    public UnquotedFieldParser(DialectDescriptor dialect)
    {
        _delimiter = dialect.Delimiter;
        _lineTerminator = dialect.LineTerminator;
        _escape = dialect.EscapeChar;
        _comment = dialect.CommentChar;
        _skipInitialSpace = dialect.SkipInitialSpace;
    }

    public ParserState Parse(char c, int pos)
    {
        if (_terminatorIndex > 0)
            return ContinueLineTerminator(c);

        if (_inComment)
        {
            if (c == _lineTerminator[0])
                return StartLineTerminator(pos);
            return ParserState.Continue;
        }

        if (_context.Escaping)
        {
            _context.EndEscaping();
            return ParserState.Continue;
        }

        var value = _context.Span.Value;
        if (!value.IsStarted)
        {
            if (_skipInitialSpace && c == ' ')
                return ParserState.Continue;

            if (_recordStart && _comment.HasValue && c == _comment.Value)
            {
                _inComment = true;
                return ParserState.Continue;
            }
        }

        if (c == _delimiter)
        {
            CompleteValue(pos - 1);
            return ParserState.Field;
        }

        if (c == _lineTerminator[0])
            return StartLineTerminator(pos);

        if (_escape.HasValue && c == _escape.Value)
        {
            StartValue(pos);
            _context.StartEscaping();
            return ParserState.Continue;
        }

        StartValue(pos);
        return ParserState.Continue;
    }

    private ParserState StartLineTerminator(int pos)
    {
        _terminatorStart = pos;
        if (_lineTerminator.Length == 1)
            return CompleteLineTerminator();

        _terminatorIndex = 1;
        return ParserState.Continue;
    }

    private ParserState ContinueLineTerminator(char c)
    {
        if (c != _lineTerminator[_terminatorIndex])
        {
            if (!_inComment && !_context.Span.Value.IsStarted)
                _context.StartValue(_terminatorStart, false);
            _terminatorIndex = 0;
            return ParserState.Reprocess;
        }

        if (++_terminatorIndex == _lineTerminator.Length)
            return CompleteLineTerminator();
        return ParserState.Continue;
    }

    private ParserState CompleteLineTerminator()
    {
        _terminatorIndex = 0;
        if (_inComment)
            return ParserState.Comment;

        CompleteValue(_terminatorStart - 1);
        return ParserState.Record;
    }

    private void StartValue(int pos)
    {
        if (!_context.Span.Value.IsStarted)
            _context.StartValue(pos, false);
    }

    private void CompleteValue(int end)
    {
        if (_context.Span.Value.IsStarted)
            _context.EndValue(end);
        else
            _context.EmptyValue();
    }

    public ParserState ParseEof(int pos)
    {
        if (_inComment)
        {
            _context.EmptyValue();
            return ParserState.Eof;
        }

        if (_terminatorIndex > 0 && !_context.Span.Value.IsStarted)
            _context.StartValue(_terminatorStart, false);
        _terminatorIndex = 0;
        CompleteValue(pos - 1);
        return ParserState.Record;
    }

    public void Reset() => Reset(true);

    public void Reset(bool recordStart)
    {
        _context.Reset();
        _recordStart = recordStart;
        _inComment = false;
        _terminatorIndex = 0;
        _terminatorStart = 0;
    }

    public ref FieldSpan Result => ref _context.Span;
}
