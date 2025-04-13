using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct DoubleQuoteParser : IParser
{
    private readonly ParserStateFn _parse;
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _quote;
    private readonly char _delimiter;

    public DoubleQuoteParser(QuotedParser parser, char delimiter, char quote)
        => (_parse, _ctx, _controller, _delimiter, _quote) =
            (parser.Parse, parser.Context, parser.Controller, delimiter, quote);

    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;

        if (c == _quote && !escaping)
        {
            _ctx.StartEscaping();
            _ctx.EndValue(pos - 1);
            return ParserState.Continue;
        }

        if (c == _quote && escaping)
        {
            _ctx.EndEscaping();
            return ParserState.Continue;
        }

        if (c == _delimiter && escaping)
        {
            _ctx.RemoveEscaping();
            return ParserState.Field;
        }

        return _parse(c, pos);
    }

    public ParserState ParseEof(int pos)
        => _ctx.IsComplete ? ParserState.Record : ParserState.Error;

    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
