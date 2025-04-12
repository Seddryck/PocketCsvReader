using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct QuotedParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    internal IParserContext Context => _ctx;
    internal IParserStateController Controller => _controller;

    private readonly char _quote;
    private readonly char? _escape;

    public QuotedParser(IParserContext ctx, IParserStateController controller, char quote, char? escape = null)
        => (_ctx, _controller, _quote, _escape) = (ctx, controller, quote, escape);

    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;
        if (c == _quote && !escaping)
        {
            _ctx.EndValue(pos - 1);
            return ParserState.Continue;
        }

        if (_escape.HasValue && c == _escape.Value && !escaping)
        {
            _ctx.StartEscaping();
            return ParserState.Continue;
        }

        if (escaping)
        {
            _ctx.EndEscaping();
            return ParserState.Continue;
        }
        return ParserState.Continue;
    }

    public ParserState ParseEof(int pos)
        => ParserState.Error;
}
