using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.CharParsing;

class NdjsonStateController : INdjsonStateController
{
    // Reusable state structs
    private readonly IParser _objectPrefixParser;
    private readonly IParser _objectSuffixParser;
    private readonly IParser _labelParser;
    private readonly IParser _separatorParser;
    private readonly IParser _valueParser;
    private readonly IParser _labelQuotedParser;
    private readonly IParser _labelRawParser;
    private readonly IParser _valueQuotedParser;
    private readonly IParser _valueRawParser;
    private readonly LineTerminatorParser _lineTerminatorParser;
    private readonly IParser? _arrayParser;
    private readonly IParser? _commentParser;

    private IParser _currentParser;
    private readonly INdjsonStateController? _parentController;
    private ParserStateFn _currentState;
    private IParser? _previousParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="NdjsonStateController"/> class, configuring internal parsers based on the specified CSV dialect.
    /// </summary>
    /// <param name="ctx">The parser context providing state and callbacks for parsing operations.</param>
    /// <param name="dialect">The dialect descriptor specifying CSV parsing rules such as delimiters, quote characters, and line terminators.</param>
    public NdjsonStateController(IParserContext ctx, NdjsonDialectDescriptor dialect)
    {
        _objectPrefixParser = new ObjectPrefixParser(ctx, this, dialect.LineTerminator, dialect.ObjectPrefix, dialect.SkipInitialSpace);
        _objectSuffixParser = new ObjectSuffixParser(ctx, this, dialect.ObjectSuffix, dialect.SkipInitialSpace);

        _labelParser = new LabelParser(ctx, this, dialect.Separator, dialect.QuoteChar,
            dialect.EscapeChar, dialect.SkipInitialSpace);

        _valueParser = new ValueParser(ctx, this, dialect.LineTerminator, dialect.Delimiter, dialect.QuoteChar,
            dialect.EscapeChar, dialect.SkipInitialSpace, dialect.ArrayPrefix);

        _labelQuotedParser = new QuotedLabelParser(ctx, this, dialect.QuoteChar!.Value, dialect.EscapeChar);
        _labelRawParser = new RawLabelParser(ctx, this, dialect.Separator);

        _valueQuotedParser = new QuotedValueParser(ctx, this, dialect.Delimiter, dialect.LineTerminator, dialect.QuoteChar!.Value, dialect.EscapeChar, dialect.ObjectSuffix);
        _valueRawParser = new RawValueParser(ctx, this, dialect.Delimiter, dialect.EscapeChar, dialect.ObjectSuffix);

        _separatorParser = new SeparatorParser(ctx, this, dialect.Separator, dialect.SkipInitialSpace);
        _lineTerminatorParser = new LineTerminatorParser(ctx, this, dialect.LineTerminator);
        //if (dialect.ArrayDelimiter.HasValue)
        //    _arrayParser = new ArrayParser(this, ctx, dialect);
        //if (dialect.CommentChar.HasValue)
        //    _commentParser = new CommentParser(ctx, this, dialect.LineTerminator);

        _currentParser = _objectPrefixParser;
        _currentState = _objectPrefixParser.Parse;
        _previousParser = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NdjsonStateController"/> class with a parent controller, parser context, and dialect descriptor.
    /// </summary>
    /// <param name="parent">The parent state controller to which control can be delegated.</param>
    /// <param name="ctx">The parser context providing parsing state and utilities.</param>
    /// <param name="dialect">The dialect descriptor specifying CSV parsing rules.</param>
    public NdjsonStateController(INdjsonStateController parent, IParserContext ctx, NdjsonDialectDescriptor dialect)
        : this(ctx, dialect)
    {
        _parentController = parent;
    }

    /// <summary>
    /// Parses a single character at the specified position using the current parser state.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The resulting parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    => _currentState(c, pos);

    /// <summary>
    /// Handles end-of-file parsing by delegating to the current parser.
    /// </summary>
    /// <param name="pos">The position in the input where EOF is encountered.</param>
    /// <returns>The resulting parser state after processing EOF.</returns>
    public ParserState ParseEof(int pos)
    => _currentParser.ParseEof(pos);

    /// <summary>
    /// Sets the specified parser as the active parser and updates the current parsing state delegate.
    /// </summary>
    protected void SwitchTo(IParser next)
    {
        _currentParser = next;
        _currentState = next.Parse;
    }

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field label.
    /// </summary>
    public void SwitchToObjectPrefix()
        => SwitchTo(_objectPrefixParser);

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field label.
    /// </summary>
    public void SwitchToLabel()
        => SwitchTo(_labelParser);

    /// <summary>
    /// Switches the active parser to the quoted field label parser.
    /// </summary>
    public void SwitchToLabelQuoted()
        => SwitchTo(_labelQuotedParser);

    /// <summary>
    /// Switches the active parser to the raw field parser for handling unquoted field label.
    /// </summary>
    public void SwitchToLabelRaw()
        => SwitchTo(_labelRawParser);

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field content.
    /// </summary>
    public void SwitchToSeparator()
        => SwitchTo(_separatorParser);

    /// <summary>
    /// Switches the active parser to the value parser for standard CSV field content.
    /// </summary>
    public void SwitchToValue()
        => SwitchTo(_valueParser);

    /// <summary>
    /// Switches the active parser to the quoted field parser.
    /// </summary>
    public void SwitchToValueQuoted()
        => SwitchTo(_valueQuotedParser);

    /// <summary>
    /// Switches the active parser to the raw field parser for handling unquoted field content.
    /// </summary>
    public void SwitchToValueRaw()
        => SwitchTo(_valueRawParser);

    /// <summary>
    /// Switches the active parser to the array parser.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the array parser is not available for the current dialect.
    /// </exception>
    public void SwitchToArray()
        => SwitchTo(_arrayParser ?? throw new InvalidOperationException());

    /// <summary>
    /// Switches the active parser to the comment parser. Throws an InvalidOperationException if comment parsing is not supported by the current dialect.
    /// </summary>
    public void SwitchToComment()
        => SwitchTo(_commentParser ?? throw new InvalidOperationException());

    /// <summary>
    /// Switches parsing to the line terminator parser, saving the current parser for later restoration and setting the return state after line termination.
    /// </summary>
    /// <param name="state">The parser state to return to after processing the line terminator.</param>
    public void SwitchToLineTerminator(ParserState state)
    {
        _previousParser = _currentParser;
        _lineTerminatorParser.ReturnState(state);
        SwitchTo(_lineTerminatorParser);
    }

    /// <summary>
    /// Resets the controller and its parsers to the initial value parsing state.
    /// </summary>
public void Reset()
{
    _lineTerminatorParser.Reset();
    if (_currentParser == _lineTerminatorParser || _currentParser == _objectPrefixParser)
        return;
    _arrayParser?.Reset();
    // Reset must put *both* delegates back to the same parser.  The object prefix
    // parser is the canonical entry point for a new record; it will internally
    // switch to the right sub‑parser for a subsequent field if we are still
    // inside the same object.
    _currentParser = _objectPrefixParser;
    _currentState  = _currentParser.Parse;
    _previousParser = null;
}

    /// <summary>
    /// Restores the previous parser state if a rollback parser is set, reverting any temporary parser switch.
    /// </summary>
    public void SwitchBack()
    {
        if (_previousParser is not null)
        {
            _currentState = _previousParser.Parse;
            _previousParser = null;
        }
    }

    /// <summary>
    /// Transfers parsing control back to the parent controller's value parser.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if there is no parent controller to switch to.
    /// </exception>
    public void SwitchUp()
    {
        if (_parentController is null)
            throw new InvalidOperationException();
        _parentController.SwitchToValue();
    }
}
