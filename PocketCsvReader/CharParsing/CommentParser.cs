using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct CommentParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;

    public CommentParser(IParserContext ctx, IParserStateController controller, string lineTerminator)
        => (_ctx, _controller, _lineTerminatorLength, _lineTerminatorChar) = (ctx, controller, lineTerminator.Length, lineTerminator[0]);

    public ParserState Parse(char c, int pos)
    {
        if (c == _lineTerminatorChar)
        {
            _ctx.StartValue(pos + _lineTerminatorLength, false);
            _controller.SwitchToLineTerminator(ParserState.Comment);
            return ParserState.Continue;
        }

        return ParserState.Continue;
    }

    public ParserState ParseEof(int pos)
    {
        _ctx.EmptyValue();
        return ParserState.Eof;
    }
    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
