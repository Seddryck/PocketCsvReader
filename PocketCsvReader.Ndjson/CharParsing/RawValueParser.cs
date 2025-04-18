using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader.Ndjson.CharParsing;
public readonly struct RawValueParser : IParser
{
    private readonly IParserContext _ctx;
    private readonly INdjsonStateController _controller;

    private readonly char _delimiter;
    private readonly char? _escape;
    private readonly char _objectSuffix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawParser"/> struct with the specified parsing context, state controller, line terminator, delimiter, and optional escape character.
    /// </summary>
    /// <param name="ctx">The parser context managing field and record state.</param>
    /// <param name="controller">The state controller for handling parser state transitions.</param>
    /// <param name="lineTerminator">The string representing the line terminator sequence.</param>
    /// <param name="delimiter">The character used to separate fields.</param>
    /// <param name="escape">An optional character used for escaping delimiters and line terminators within fields.</param>
    public RawValueParser(IParserContext ctx, INdjsonStateController controller, char delimiter, char? escape, char objectSuffix)
    => (_ctx, _controller, _delimiter, _escape, _objectSuffix) = (ctx, controller, delimiter, escape, objectSuffix);

    /// <summary>
    /// Processes a single character during CSV parsing, updating the parser state based on delimiters, escape characters, and line terminators.
    /// </summary>
    /// <param name="c">The character to parse.</param>
    /// <param name="pos">The position of the character in the input.</param>
    /// <returns>The next parser state after processing the character.</returns>
    public ParserState Parse(char c, int pos)
    {
        var escaping = _ctx.Escaping;
        var complete = _ctx.Span.Value.IsComplete;

        if (c == _delimiter  && !escaping)
        {
            if (!complete)
                _ctx.EndValue(pos - 1);
            return ParserState.Field;
        }

        if (c == _objectSuffix && !escaping)
        {
            if (!complete)
                _ctx.EndValue(pos - 1);
            _controller.SwitchToLineTerminator(ParserState.Continue);
            return ParserState.Record;
        }

        if (c == ' ' && !complete)
            _ctx.EndValue(pos - 1);
        else if (_escape.HasValue && c == _escape.Value && !escaping)
            _ctx.StartEscaping();
        else if (escaping)
            _ctx.EndEscaping();

        return ParserState.Continue;
    }

    /// <summary>
    /// Handles end-of-file by finalizing the current field and signaling the end of the record.
    /// </summary>
    /// <param name="pos">The position immediately after the last character in the input.</param>
    /// <returns>The parser state indicating the end of a record.</returns>
    public ParserState ParseEof(int pos)
        => ParserState.Eof;

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
