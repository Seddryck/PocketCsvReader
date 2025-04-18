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
    /// Initializes a new instance of the <see cref="SeparatorParser"/> struct with the specified field parser context.
    /// </summary>
    public SeparatorParser(IParserContext ctx, INdjsonStateController controller, char separator, bool skipInitialSpace)
        => (_ctx, _controller, _separator, _skipInitialSpace) =
            (ctx, controller, separator, skipInitialSpace);

    /// <summary>
    /// Processes a single character during NDJSON parsing, looking for the separator character and updating parser state accordingly.
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
    /// Handles end-of-file condition, always returning an error state since a separator is required.
    /// </summary>
    /// <param name="pos">The position in the input where end-of-file is detected.</param>
    /// <returns>Always returns ParserState.Error since finding EOF before the separator is an error condition.</returns>
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
