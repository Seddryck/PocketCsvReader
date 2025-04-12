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

    public ArrayParser(IParserStateController parent, IParserContext ctx, DialectDescriptor dialect)
    {
        _parent = parent;
        _ctx = new FieldContext(ctx);
        (_dialect, _suffixArray, _delimiter, _quote, _escape, _skipInitialSpace)
            = (dialect, dialect.ArraySuffix, dialect.ArrayDelimiter!.Value, dialect.QuoteChar,
                dialect.EscapeChar, dialect.SkipInitialSpace);
    }

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

    public ParserState ParseEof(int pos)
        => ParserState.Error;
}
