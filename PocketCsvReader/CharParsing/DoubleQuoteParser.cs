using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal readonly struct DoubleQuoteParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly IParserStateController _controller;

    private readonly char _quote;
    private readonly char? _escape;
    private readonly char _delimiter;
    private readonly char _lineTerminatorChar;
    private readonly int _lineTerminatorLength;

    /// <summary>
            /// Initializes a new instance of the <see cref="DoubleQuoteParser"/> for parsing CSV fields enclosed in double quotes, with configurable delimiter, quote, escape character, and line terminator.
            /// </summary>
            /// <param name="ctx">The parser context used to track parsing state and field spans.</param>
            /// <param name="controller">The state controller managing multi-character line terminators and parsing transitions.</param>
            /// <param name="delimiter">The character used to separate fields.</param>
            /// <param name="lineTerminator">The string representing the line terminator sequence.</param>
            /// <param name="quote">The character used to enclose quoted fields.</param>
            /// <param name="escape">An optional character used for escaping within quoted fields.</param>
            public DoubleQuoteParser(IParserContext ctx, IParserStateController controller, char delimiter, string lineTerminator, char quote, char? escape = null)
        => (_ctx, _controller, _delimiter, _lineTerminatorChar, _lineTerminatorLength, _quote, _escape) =
            (ctx, controller, delimiter, lineTerminator[0], lineTerminator.Length, quote, escape);

    /// <summary>
    /// Processes a single character in a quoted CSV field, updating the parsing state based on escaping, quote doubling, delimiters, and line terminators.
    /// </summary>
    /// <param name="c">The current character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The next parser state indicating whether to continue, complete a field or record, or signal an error.</returns>
    public ParserState Parse(char c, int pos)
    {
        var doubling = _ctx.Doubling;
        var escaping = _ctx.Escaping;

        if (escaping)
        {
            _ctx.EndEscaping();
            return ParserState.Continue;
        }

        if (_escape.HasValue && c == _escape.Value && !escaping)
        {
            if (doubling)
                return ParserState.Error;
            _ctx.StartEscaping();
            return ParserState.Continue;
        }

        if (c == _quote && !doubling)
        {
            if (escaping)
            {
                _ctx.EndEscaping();
                return ParserState.Continue;
            }
            _ctx.StartDoubling();
            _ctx.EndValue(pos - 1);
            return ParserState.Continue;
        }

        if (_ctx.IsComplete || doubling)
        {
            if (c == _quote)
            {
                _ctx.EndDoubling();
                return ParserState.Continue;
            }

            if (c == _delimiter)
            {
                _ctx.RemoveDoubling();
                return ParserState.Field;
            }
                
            if (c == _lineTerminatorChar)
            {
                if (_lineTerminatorLength == 1)
                    return ParserState.Record;
                _controller.SwitchToLineTerminator(ParserState.Record);
                return ParserState.Continue;
            }
            return ParserState.Error;
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
