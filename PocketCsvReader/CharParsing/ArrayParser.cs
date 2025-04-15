using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.CharParsing;
struct ArrayParser : IParser
{
    private readonly IParserContext _ctx;
    private IParserStateController? _internalController;
    private IParserStateController _parent;

    private IParserStateController _controller
        => _internalController ??= new FieldStateController(_parent, _ctx, CreateInnerDialect(_dialect));

    /// <summary>
    /// Creates a new dialect descriptor for parsing array elements by replacing the delimiter with the array delimiter and disabling array nesting.
    /// </summary>
    /// <param name="outer">The outer dialect descriptor to base the new dialect on.</param>
    /// <returns>A dialect descriptor configured for array element parsing.</returns>
    private DialectDescriptor CreateInnerDialect(DialectDescriptor outer)
    {
        return outer with
        {
            Delimiter = outer.ArrayDelimiter!.Value, // Use array delimiter
            ArrayPrefix = null,
            ArraySuffix = null,
            ArrayDelimiter = null // prevent nesting for now, or allow it if needed
        };
    }

    private readonly char _delimiter;
    private readonly char? _quote;
    private readonly char? _escape;
    private readonly bool _skipInitialSpace;
    private readonly char? _suffixArray;
    private readonly DialectDescriptor _dialect;

    /// <summary>
    /// Initializes a new ArrayParser for parsing array-like CSV fields using the specified parent controller, context, and dialect settings.
    /// </summary>
    public ArrayParser(IParserStateController parent, IParserContext ctx, DialectDescriptor dialect)
    {
        _parent = parent;
        _ctx = new FieldContext(ctx);
        (_dialect, _suffixArray, _delimiter, _quote, _escape, _skipInitialSpace)
            = (dialect, dialect.ArraySuffix, dialect.ArrayDelimiter!.Value, dialect.QuoteChar,
                dialect.EscapeChar, dialect.SkipInitialSpace);
    }

    /// <summary>
    /// Parses a character within an array field, handling array suffix detection and delegating element parsing to the internal controller.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The resulting parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;
        if (_suffixArray.HasValue && c == _suffixArray.Value && !escaping)
        {
            if (!_ctx.Span.Value.WasQuoted)
                _ctx.EndValue(pos - 1);
            _ctx.Parent!.AddChild(_ctx.Span);
            _ctx.Reset();
            //_controller.Reset();
            _controller.SwitchUp();
            return ParserState.Continue;
        }

        var inner = _controller.Parse(c, pos);
        if (inner == ParserState.Field)
        {
            _ctx.Parent!.AddChild(_ctx.Span);
            _ctx.Reset();
            _controller.Reset();
            return ParserState.Continue;
        }
        return inner;
    }

    /// <summary>
        /// Returns an error state when end-of-file is reached, indicating incomplete array parsing.
        /// </summary>
        public ParserState ParseEof(int pos)
        => ParserState.Error;

    /// <summary>
    /// Resets the parser context and internal controller to their initial states.
    /// </summary>
    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
