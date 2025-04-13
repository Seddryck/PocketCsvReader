using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.CharParsing;
internal readonly struct ValueParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _lineTerminator;
    private readonly char _delimiter;
    private readonly char? _quote;
    private readonly char? _escape;
    private readonly bool _skipInitialSpace;
    private readonly bool _doubleQuote;
    private readonly char? _comment;
    private readonly char? _prefixArray;

    public ValueParser(IParserContext ctx, IParserStateController controller, string lineTerminator, char delimiter,
        char? quote = null, char? escape = null, bool skipInitialSpace = false, bool doubleQuote = false, char? comment = null, char? prefixArray = null)
        => (_ctx, _controller, _lineTerminator, _delimiter, _quote, _escape, _skipInitialSpace, _doubleQuote, _comment, _prefixArray)
            = (ctx, controller, lineTerminator[0], delimiter, quote, escape, skipInitialSpace, doubleQuote, comment, prefixArray);

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
            if (_ctx.Span.Value.IsStarted)
                _ctx.EndValue(pos - (_ctx.Span.Value.WasQuoted ? 2 : 0)); //wasQuoted implies an array!
            else
                _ctx.EmptyValue();
            return ParserState.Field;
        }

        if (_comment.HasValue && c == _comment.Value)
        {
            _controller.SwitchToComment();
            return ParserState.Continue;
        }

        if (c == _lineTerminator)
        {
            if (_ctx.Span.Value.IsStarted)
                _ctx.EndValue(pos);
            else
                _ctx.EmptyValue();
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
    {
        if (_ctx.Span.Value.IsStarted)
            _ctx.EndValue(pos - (_ctx.Span.Value.WasQuoted ? 2 : 0)); //for arrays
        else
            _ctx.EmptyValue();

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
