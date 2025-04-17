using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Ndjson.CharParsing;
public readonly struct SeparatorParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly INdjsonStateController _controller;

    private readonly char _separator;
    private readonly bool _skipInitialSpace;

    /// <summary>
    /// Initializes a new instance of the <see cref="LabelParser"/> struct with the specified field parser context.
    /// </summary>
    public SeparatorParser(IParserContext ctx, INdjsonStateController controller, char separator, bool skipInitialSpace)
        => (_ctx, _controller, _separator, _skipInitialSpace) =
            (ctx, controller, separator, skipInitialSpace);

    /// <summary>
    /// Processes a single character during CSV parsing, updating the parser state based on delimiters, quotes, escapes, comments, array prefixes, and line terminators.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The resulting parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    {
        if (_skipInitialSpace && c == ' ')
        {
            return ParserState.Continue;
        }

        if (c == _separator)
        {
            _controller.SwitchToValue();
            return ParserState.Continue;
        }

        return ParserState.Error;
    }

    /// <summary>
    /// Finalizes the current value at end-of-file, marking it as complete or empty as appropriate, and returns a record boundary state.
    /// </summary>
    /// <param name="pos">The position in the input where end-of-file is detected.</param>
    /// <returns>A parser state indicating the end of a record.</returns>
    public ParserState ParseEof(int pos)
        => ParserState.Error;

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
