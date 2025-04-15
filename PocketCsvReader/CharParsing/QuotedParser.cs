using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct QuotedParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    internal IParserContext Context => _ctx;
    internal IParserStateController Controller => _controller;

    private readonly char _delimiter;
    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;
    private readonly char _quote;
    private readonly char? _escape;

    /// <summary>
        /// Initializes a new instance of the <see cref="QuotedParser"/> struct for parsing quoted fields with the specified context, controller, delimiter, line terminator, quote, and optional escape character.
        /// </summary>
        /// <param name="ctx">The parser context managing field state and span.</param>
        /// <param name="controller">The state controller for managing parser transitions.</param>
        /// <param name="delimiter">The character used to separate fields.</param>
        /// <param name="lineTerminator">The string representing the line terminator.</param>
        /// <param name="quote">The character used for quoting fields.</param>
        /// <param name="escape">An optional character used for escaping within quoted fields.</param>
        public QuotedParser(IParserContext ctx, IParserStateController controller, char delimiter, string lineTerminator, char quote, char? escape = null)
        => (_ctx, _controller, _delimiter, _lineTerminatorChar, _lineTerminatorLength, _quote, _escape) = (ctx, controller, delimiter, lineTerminator[0], lineTerminator.Length, quote, escape);

    /// <summary>
    /// Processes a single character within a quoted CSV field, updating the parsing state based on quotes, delimiters, line terminators, and escape sequences.
    /// </summary>
    /// <param name="c">The current character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The next parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;
        var isComplete = _ctx.IsComplete;

        if (isComplete)
        {
            if (c == _delimiter)
                return ParserState.Field;
            if (c == _lineTerminatorChar)
            {
                if (_lineTerminatorLength == 1)
                    return ParserState.Record;
                _controller.SwitchToLineTerminator(ParserState.Record);
                return ParserState.Continue;
            }
            return ParserState.Error;
        }

        if (c == _quote && !escaping)
        {
            _ctx.EndValue(pos - 1);
            return ParserState.Continue;
        }

        if (_escape.HasValue && c == _escape.Value && !escaping)
        {
            _ctx.StartEscaping();
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
        /// Determines the parser state at end-of-file, returning <c>Record</c> if the quoted field is complete, or <c>Error</c> otherwise.
        /// </summary>
        /// <param name="pos">The current character position in the input.</param>
        /// <returns><c>ParserState.Record</c> if the field is complete; otherwise, <c>ParserState.Error</c>.</returns>
        public ParserState ParseEof(int pos)
        => _ctx.IsComplete ? ParserState.Record : ParserState.Error;
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
