using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.CharParsing;

/// <summary>
/// Parses the scalar values contained in a JSON array and stores their spans as
/// children of the enclosing field.
/// </summary>
internal struct ArrayParser : IParser
{
    private readonly IParserContext _context;
    private readonly IParserStateController _parentController;
    private readonly char _delimiter;
    private readonly char _suffix;
    private readonly char[] _whitespaces;
    private IParserStateController? _internalController;
    private int _lastNonWhitespacePosition;

    private IParserStateController Controller
        => _internalController ??= new FieldStateController(
            _parentController,
            _context,
            _dialect);

    private readonly DialectDescriptor _dialect;

    public ArrayParser(IParserStateController parentController, IParserContext context, NdjsonDialectDescriptor dialect)
    {
        if (dialect.ArrayDelimiter is not char delimiter || dialect.ArraySuffix is not char suffix)
            throw new ArgumentException("The NDJSON dialect must define an array delimiter and suffix.", nameof(dialect));

        _parentController = parentController;
        _context = new FieldContext(context);
        _delimiter = delimiter;
        _suffix = suffix;
        _whitespaces = dialect.Whitespaces;
        _lastNonWhitespacePosition = -1;
        _internalController = null;
        _dialect = new DialectDescriptor(
            Header: false,
            Delimiter: delimiter,
            LineTerminator: dialect.LineTerminator,
            QuoteChar: dialect.QuoteChar,
            DoubleQuote: false,
            EscapeChar: dialect.EscapeChar,
            SkipInitialSpace: dialect.SkipInitialSpace);
    }

    public ParserState Parse(char c, int pos)
    {
        var value = _context.Span.Value;
        var suffixClosesArray = c == _suffix && (!value.WasQuoted || value.IsComplete);

        if (suffixClosesArray)
        {
            AddCurrentItem();
            var parent = _context.Parent ?? throw new InvalidOperationException("Array context has no parent.");
            parent.Span.Children ??= [];
            parent.EndValue(pos - 1);
            _context.Reset();

            // A completed array follows the same outer transitions as a completed
            // quoted value: optional whitespace, then either ',' or '}'.
            _parentController.SwitchToQuoted();
            return ParserState.Continue;
        }

        if (!_context.Span.Value.IsStarted && _whitespaces.Contains(c))
            return ParserState.Continue;

        if (c != _delimiter && !_whitespaces.Contains(c))
            _lastNonWhitespacePosition = pos;

        var state = Controller.Parse(c, pos);
        if (state == ParserState.Field)
        {
            AddCurrentItem();
            _context.Reset();
            Controller.Reset();
            _lastNonWhitespacePosition = -1;
            return ParserState.Continue;
        }

        return state;
    }

    private void AddCurrentItem()
    {
        if (!_context.Span.Value.IsStarted)
            return;

        if (!_context.Span.Value.WasQuoted)
            _context.EndValue(_lastNonWhitespacePosition);

        var parent = _context.Parent ?? throw new InvalidOperationException("Array context has no parent.");
        parent.AddChild(_context.Span);
    }

    public ParserState ParseEof(int pos)
        => ParserState.Error;

    public void Reset()
    {
        _context.Reset();
        _internalController?.Reset();
        _lastNonWhitespacePosition = -1;
    }

    public ref FieldSpan Result
        => ref _context.Span;
}
