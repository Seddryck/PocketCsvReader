using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

class FieldStateController : IParserStateController
{
    // Reusable state structs
    private readonly IParser _valueParser;
    private readonly IParser _quotedParser;
    private readonly IParser _rawParser;
    private readonly IParser _lineTerminatorParser;
    private readonly IParser? _arrayParser;
    private readonly IParser? _commentParser;

    private IParser _currentParser;
    private readonly IParserStateController? _parentController;
    private ParserStateFn _currentState;
    private IParser? _rollback;

    public FieldStateController(IParserContext ctx, DialectDescriptor dialect)
    {
        _valueParser = new ValueParser(ctx, this, dialect.LineTerminator, dialect.Delimiter, dialect.QuoteChar,
            dialect.EscapeChar, dialect.SkipInitialSpace, dialect.DoubleQuote, dialect.CommentChar, dialect.ArrayPrefix);
        _quotedParser = new QuotedParser(ctx, this, dialect.Delimiter, dialect.LineTerminator, dialect.QuoteChar!.Value, dialect.EscapeChar);
        if (dialect.DoubleQuote)
            _quotedParser = new DoubleQuoteParser((QuotedParser)_quotedParser, dialect.Delimiter, dialect.QuoteChar!.Value);
        _rawParser = new RawParser(ctx, this, dialect.LineTerminator, dialect.Delimiter, dialect.EscapeChar);
        _lineTerminatorParser = new LineTerminatorParser(ctx, this, dialect.LineTerminator);
        if (dialect.ArrayDelimiter.HasValue)
            _arrayParser = new ArrayParser(this, ctx, dialect);
        if (dialect.CommentChar.HasValue)
            _commentParser = new CommentParser(ctx, this, dialect.LineTerminator);

        _currentParser = _valueParser;
        _currentState = _valueParser.Parse;
        _rollback = null;
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

    protected void SwitchTo(IParser next)
    {
        _currentParser = next;
        _currentState = next.Parse;
    }

    public void SwitchToValue()
        => SwitchTo(_valueParser);
    public void SwitchToQuoted()
        => SwitchTo(_quotedParser);
    public void SwitchToRaw()
        => SwitchTo(_rawParser);
    public void SwitchToArray()
        => SwitchTo(_arrayParser ?? throw new InvalidOperationException());
    public void SwitchToComment()
        => SwitchTo(_commentParser ?? throw new InvalidOperationException());
    public void SwitchToLineTerminator()
    {
        _rollback = _currentParser;
        SwitchTo(_lineTerminatorParser);
    }

    public void Reset()
    {
        _lineTerminatorParser.Reset();
        _currentState = _valueParser.Parse;
        _currentParser = _valueParser;
        _rollback = null;
    }

    public void SwitchBack()
    {
        if (_rollback is not null)
        {
            _currentState = _rollback.Parse;
            _rollback = null;
        }
    }

    public void SwitchUp()
    {
        if (_parentController is null)
            throw new InvalidOperationException();
        _parentController.SwitchToValue();
    }
}
