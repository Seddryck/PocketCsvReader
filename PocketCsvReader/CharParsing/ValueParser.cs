using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.CharParsing;
readonly struct ValueParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _lineTerminator;
    private readonly char _delimiter;
    private readonly char? _quote;
    private readonly char? _escape;
    private readonly bool _skipInitialSpace;
    private readonly char? _prefixArray;

    public ValueParser(IParserContext ctx, IParserStateController controller, string lineTerminator, char delimiter,
        char? quote = null, char? escape = null, bool skipInitialSpace = false, bool doubleQuote = false, char? prefixArray = null)
        => (_ctx, _controller, _lineTerminator, _delimiter, _quote, _escape, _skipInitialSpace, _prefixArray)
            = (ctx, controller, lineTerminator[0], delimiter, quote, escape, skipInitialSpace, prefixArray);

    public ParserState Parse(char c, int pos)
    {
        if (_quote.HasValue && c == _quote.Value)
        {
            _ctx.StartValue(pos, true);
            _controller.SwitchToQuoted();
            return ParserState.Continue;
        }

        if (_prefixArray.HasValue && c == _prefixArray.Value)
        {
            _ctx.StartValue(pos, true);
            _controller.SwitchToArray();
            return ParserState.Continue;
        }

        if (_skipInitialSpace && c == ' ')
        {
            return ParserState.Continue;
        }

        if (c == _delimiter)
        {
            _ctx.EndValue(pos);
            return ParserState.Field;
        }

        if (c == _lineTerminator && !_ctx.Escaping)
        {
            _ctx.EndValue(pos);
            _controller.SwitchToLineTerminator();
            return ParserState.Continue;
        }

        if (_escape.HasValue && c == _escape.Value)
            _ctx.StartEscaping();

        _ctx.StartValue(pos, false);
        _controller.SwitchToRaw();
        return ParserState.Continue;
    }

    public ParserState ParseEof(int pos)
        => ParserState.Record;
}
