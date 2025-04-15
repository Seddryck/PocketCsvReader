using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct RawParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _delimiter;
    private readonly char? _escape;
    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;

    public RawParser(IParserContext ctx, IParserStateController controller, string lineTerminator, char delimiter, char? escape = null)
        => (_ctx, _controller, _lineTerminatorLength, _lineTerminatorChar, _delimiter, _escape) = (ctx, controller, lineTerminator.Length, lineTerminator[0], delimiter, escape);

    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;
        if (c == _delimiter && !escaping)
        {
            _ctx.EndValue(pos - 1);
            return ParserState.Field;
        }

        if (_escape.HasValue && c == _escape.Value && !escaping)
        {
            _ctx.StartEscaping();
            return ParserState.Continue;
        }

        if (c == _lineTerminatorChar && !escaping)
        {
            _ctx.EndValue(pos - 1);
            if (_lineTerminatorLength == 1)
                return ParserState.Record;
            _controller.SwitchToLineTerminator(ParserState.Record);
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
    {
        _ctx.EndValue(pos - 1);
        return ParserState.Record;
    }
    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
