using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Ndjson.CharParsing;
public readonly struct ObjectSuffixParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly INdjsonStateController _controller;

    private readonly char _objectSuffix;
    private readonly bool _skipInitialSpace;

    /// <summary>
    /// Initializes a new instance of the<see cref = "ObjectSuffixParser" /> struct with the specified field parser context.
    /// </summary>
    public ObjectSuffixParser(IParserContext ctx, INdjsonStateController controller, char objectSuffix, bool skipInitialSpace)
        => (_ctx, _controller, _objectSuffix, _skipInitialSpace) =
            (ctx, controller, objectSuffix, skipInitialSpace);

    /// <summary>
    /// Processes a single character during NDJSON parsing, updating the parser state based on delimiters, quotes, escapes, comments, array prefixes, and line terminators.
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

        if (c == _objectSuffix)
        {
            _controller.SwitchToValue();
            return ParserState.Continue;
        }

        return ParserState.Error;
    }

    /// <summary>
    /// Handles end-of-file condition, always returning an error state since object suffix is required.
    /// </summary>
    /// <param name="pos">The position in the input where end-of-file is detected.</param>
    /// <returns>Always returns ParserState.Error since finding EOF before the object suffix is an error condition.</returns>
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
