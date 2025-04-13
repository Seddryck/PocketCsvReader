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

    private readonly char _delimiter;
    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;
    private readonly char _quote;
    private readonly char? _escape;

    public QuotedParser(IParserContext ctx, IParserStateController controller, char delimiter, string lineTerminator, char quote, char? escape = null)
        => (_ctx, _controller, _delimiter, _lineTerminatorChar, _lineTerminatorLength, _quote, _escape) = (ctx, controller, delimiter, lineTerminator[0], lineTerminator.Length, quote, escape);

    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;

        if (_ctx.IsComplete)
        {
            if (c == _delimiter)
                return ParserState.Field;
            if (c == _lineTerminatorChar)
            {
                if (_lineTerminatorLength == 1)
                    return ParserState.Record;
                _controller.SwitchToLineTerminator();
                return ParserState.Continue;
            }
            return ParserState.Error;
        }

        if (c == _quote && !escaping )
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
        => _ctx.IsComplete ? ParserState.Record : ParserState.Error;
    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
