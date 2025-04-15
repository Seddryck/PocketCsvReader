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

    /// <summary>
        /// Initializes a new instance of the <see cref="CommentParser"/> with the specified parser context, state controller, and line terminator.
        /// </summary>
        /// <param name="ctx">The parser context used to manage parsing state and values.</param>
        /// <param name="controller">The state controller responsible for managing parser state transitions.</param>
        /// <param name="lineTerminator">The string representing the line terminator sequence.</param>
        public CommentParser(IParserContext ctx, IParserStateController controller, string lineTerminator)
        => (_ctx, _controller, _lineTerminatorLength, _lineTerminatorChar) = (ctx, controller, lineTerminator.Length, lineTerminator[0]);

    /// <summary>
    /// Processes a character while parsing a comment section, handling line terminators and advancing the parser state as needed.
    /// </summary>
    /// <param name="c">The current character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>
    /// <see cref="ParserState.Continue"/> after processing the character.
    /// </returns>
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

    /// <summary>
    /// Handles end-of-file by marking the current value as empty and returning the EOF parser state.
    /// </summary>
    /// <param name="pos">The current position in the input.</param>
    /// <returns>The EOF parser state.</returns>
    public ParserState ParseEof(int pos)
    {
        _ctx.EmptyValue();
        return ParserState.Eof;
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
