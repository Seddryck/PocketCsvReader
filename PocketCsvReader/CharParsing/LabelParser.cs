using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public readonly struct LabelParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _separator;
    private readonly char? _quote;
    private readonly char? _escape;
    private readonly bool _skipInitialSpace;
    private readonly char? _comment;

    /// <summary>
    /// Initializes a new instance of the <see cref="LabelParser"/> struct with the specified field parser context.
    /// </summary>
    public LabelParser(IParserContext ctx, IParserStateController controller, char separator, char? quote,
            char? escape, bool skipInitialSpace, char? comment)
        => (_ctx, _controller, _separator, _quote, _escape, _skipInitialSpace, _comment) =
            (ctx, controller, separator, quote, escape, skipInitialSpace, comment);

    /// <summary>
    /// Processes a single character during CSV parsing, updating the parser state based on delimiters, quotes, escapes, comments, array prefixes, and line terminators.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The resulting parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    {
        if (_quote.HasValue && c == _quote.Value)
        {
            _ctx.StartValue(pos, true);
            _controller.SwitchToQuoted();
            return ParserState.Continue;
        }

        if (_skipInitialSpace && c == ' ')
        {
            return ParserState.Continue;
        }

        if (c == _separator)
            return ParserState.Error;

        if (_comment.HasValue && c == _comment.Value)
        {
            _controller.SwitchToComment();
            return ParserState.Continue;
        }

        if (_escape.HasValue && c == _escape.Value)
            _ctx.StartEscaping();

        _ctx.StartValue(pos, false);
        _controller.SwitchToRaw();
        return ParserState.Continue;
    }

    /// <summary>
    /// Finalizes the current value at end-of-file, marking it as complete or empty as appropriate, and returns a record boundary state.
    /// </summary>
    /// <param name="pos">The position in the input where end-of-file is detected.</param>
    /// <returns>A parser state indicating the end of a record.</returns>
    public ParserState ParseEof(int pos)
    {
        if (_ctx.Span.Value.IsStarted)
            _ctx.EndValue(pos - (_ctx.Span.Value.WasQuoted ? 2 : 0)); //for arrays
        else
            _ctx.EmptyValue();

        return ParserState.Record;
    }
    /// <summary>
    /// Resets the parser context and state controller to their initial states.
    /// </summary>
    public void Reset()
    {
        _ctx.Reset();
        _controller.Reset();
    }

    public ref FieldSpan Result
        => ref _ctx.Span;
}
