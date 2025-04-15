using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
class LineTerminatorParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char[] _lineTerminators;
    private int _index = 0;
    private ParserState _returnState = ParserState.Record;

    public LineTerminatorParser(IParserContext ctx, IParserStateController controller, string lineTerminator)
        => (_ctx, _controller, _lineTerminators)
            = (ctx, controller, lineTerminator.ToCharArray());

    public ParserState Parse(char c, int pos)
    {
        if (c == _lineTerminators[++_index])
        {
            if (_index == _lineTerminators.Length - 1)
                return _returnState;
            return ParserState.Continue;
        }
        else
        {
            Reset();
            _controller.SwitchBack();
            return ParserState.Continue;
        }
    }

    public ParserState ParseEof(int pos)
        => _returnState;
    public void Reset()
    {
        _index = 0;
        _returnState = ParserState.Record;
    }

    public void ReturnState(ParserState state)
    {
        _returnState = state;
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
