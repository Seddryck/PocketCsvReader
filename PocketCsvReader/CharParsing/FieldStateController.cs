namespace PocketCsvReader.CharParsing;

public class FieldStateController : IParserStateController
{
    public bool IsRecordStart { get; private set; } = true;
    public void SetRecordStart(bool value) => IsRecordStart = value;

    private readonly IParser _valueParser;
    private readonly IParser _quotedParser;
    private readonly IParser _rawParser;
    private readonly LineTerminatorParser _lineTerminatorParser;
    private readonly IParser? _arrayParser;
    private readonly IParser? _commentParser;
    private readonly ParserStateFn _valueState;
    private readonly ParserStateFn _quotedState;
    private readonly ParserStateFn _rawState;
    private readonly ParserStateFn _lineTerminatorState;
    private readonly ParserStateFn? _arrayState;
    private readonly ParserStateFn? _commentState;

    private readonly IParserStateController? _parentController;
    private IParser _currentParser;
    private ParserStateFn _currentState;
    private IParser? _previousParser;
    private ParserStateFn? _previousState;

    public FieldStateController(IParserContext ctx, DialectDescriptor dialect)
    {
        _valueParser = new ValueParser(ctx, this, dialect.LineTerminator, dialect.Delimiter, dialect.QuoteChar,
            dialect.EscapeChar, dialect.SkipInitialSpace, dialect.DoubleQuote, dialect.CommentChar, dialect.ArrayPrefix);

        _quotedParser = dialect.DoubleQuote
            ? new DoubleQuoteParser(ctx, this, dialect.Delimiter, dialect.LineTerminator,
                dialect.QuoteChar.GetValueOrDefault(), dialect.EscapeChar)
            : new QuotedParser(ctx, this, dialect.Delimiter, dialect.LineTerminator,
                dialect.QuoteChar.GetValueOrDefault(), dialect.EscapeChar);

        _rawParser = new RawParser(ctx, this, dialect.LineTerminator, dialect.Delimiter, dialect.EscapeChar);
        _lineTerminatorParser = new LineTerminatorParser(ctx, this, dialect.LineTerminator);
        if (dialect.ArrayDelimiter.HasValue)
            _arrayParser = new ArrayParser(this, ctx, dialect);
        if (dialect.CommentChar.HasValue)
            _commentParser = new CommentParser(ctx, this, dialect.LineTerminator);

        _valueState = _valueParser.Parse;
        _quotedState = _quotedParser.Parse;
        _rawState = _rawParser.Parse;
        _lineTerminatorState = _lineTerminatorParser.Parse;
        if (_arrayParser is not null)
            _arrayState = _arrayParser.Parse;
        if (_commentParser is not null)
            _commentState = _commentParser.Parse;
        _currentParser = _valueParser;
        _currentState = _valueState;
    }

    public FieldStateController(IParserStateController parent, IParserContext ctx, DialectDescriptor dialect)
        : this(ctx, dialect)
    {
        _parentController = parent;
    }

    public ParserState Parse(char c, int pos)
        => _currentState(c, pos);

    public ParserState ParseEof(int pos)
        => _currentParser.ParseEof(pos);

    private void SwitchTo(IParser next, ParserStateFn nextState)
    {
        _currentParser = next;
        _currentState = nextState;
    }

    public void SwitchToValue() => SwitchTo(_valueParser, _valueState);
    public void SwitchToQuoted() => SwitchTo(_quotedParser, _quotedState);
    public void SwitchToRaw() => SwitchTo(_rawParser, _rawState);
    public void SwitchToArray() => SwitchTo(_arrayParser ?? throw new InvalidOperationException(),
        _arrayState ?? throw new InvalidOperationException());
    public void SwitchToComment() => SwitchTo(_commentParser ?? throw new InvalidOperationException(),
        _commentState ?? throw new InvalidOperationException());

    public void SwitchToLineTerminator(ParserState state)
    {
        _previousParser = _currentParser;
        _previousState = _currentState;
        _lineTerminatorParser.ReturnState(state);
        SwitchTo(_lineTerminatorParser, _lineTerminatorState);
    }

    public void Reset()
    {
        _lineTerminatorParser.Reset();
        _arrayParser?.Reset();
        SwitchTo(_valueParser, _valueState);
        _previousParser = null;
        _previousState = null;
    }

    public void SwitchBack()
    {
        if (_previousParser is not null && _previousState is not null)
        {
            SwitchTo(_previousParser, _previousState);
            _previousParser = null;
            _previousState = null;
        }
    }

    public void SwitchUp()
    {
        if (_parentController is null)
            throw new InvalidOperationException();
        _parentController.SwitchToValue();
    }
}
