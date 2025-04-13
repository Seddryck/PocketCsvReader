using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
struct LineTerminatorParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char[] _lineTerminators;
    private int _index = 0;

    public LineTerminatorParser(IParserContext ctx, IParserStateController controller, string lineTerminator)
        => (_ctx, _controller, _lineTerminators)
            = (ctx, controller, lineTerminator.ToCharArray());

    public ParserState Parse(char c, int pos)
    {
        if (c == _lineTerminators[++_index])
        {
            if (_index == _lineTerminators.Length - 1)
                return ParserState.Record;
            return ParserState.Continue;
        }
        else
        {
            _controller.SwitchBack();
            return ParserState.Continue;
        }
    }

    public ParserState ParseEof(int pos)
        => ParserState.Error;
    public void Reset()
    {
        _index = 0;
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
