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

    /// <summary>
        /// Initializes a new instance of the <see cref="RawParser"/> struct with the specified parsing context, state controller, line terminator, delimiter, and optional escape character.
        /// </summary>
        /// <param name="ctx">The parser context managing field and record state.</param>
        /// <param name="controller">The state controller for handling parser state transitions.</param>
        /// <param name="lineTerminator">The string representing the line terminator sequence.</param>
        /// <param name="delimiter">The character used to separate fields.</param>
        /// <param name="escape">An optional character used for escaping delimiters and line terminators within fields.</param>
        public RawParser(IParserContext ctx, IParserStateController controller, string lineTerminator, char delimiter, char? escape = null)
        => (_ctx, _controller, _lineTerminatorLength, _lineTerminatorChar, _delimiter, _escape) = (ctx, controller, lineTerminator.Length, lineTerminator[0], delimiter, escape);

    /// <summary>
    /// Processes a single character during CSV parsing, updating the parser state based on delimiters, escape characters, and line terminators.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The next parser state after processing the character.</returns>
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

    /// <summary>
    /// Handles end-of-file by finalizing the current field and signaling the end of the record.
    /// </summary>
    /// <param name="pos">The position immediately after the last character in the input.</param>
    /// <returns>The parser state indicating the end of a record.</returns>
    public ParserState ParseEof(int pos)
    {
        _ctx.EndValue(pos - 1);
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
