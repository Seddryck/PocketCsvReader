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

    /// <summary>
            /// Initializes a new instance of the <see cref="ValueParser"/> struct with the specified parsing context, state controller, and CSV parsing configuration.
            /// </summary>
            /// <param name="ctx">The parser context used to manage field spans and parsing state.</param>
            /// <param name="controller">The state controller responsible for managing parser state transitions.</param>
            /// <param name="lineTerminator">The string representing the line terminator; only the first character is used.</param>
            /// <param name="delimiter">The character used to separate fields.</param>
            /// <param name="quote">Optional character used for quoting field values.</param>
            /// <param name="escape">Optional character used for escaping within quoted fields.</param>
            /// <param name="skipInitialSpace">Indicates whether to skip spaces following delimiters.</param>
            /// <param name="doubleQuote">Indicates whether double quotes are used to escape quotes within quoted fields.</param>
            /// <param name="comment">Optional character indicating the start of a comment.</param>
            /// <param name="prefixArray">Optional character indicating the start of an array value.</param>
            public ValueParser(IParserContext ctx, IParserStateController controller, string lineTerminator, char delimiter,
        char? quote = null, char? escape = null, bool skipInitialSpace = false, bool doubleQuote = false, char? comment = null, char? prefixArray = null)
        => (_ctx, _controller, _lineTerminator, _delimiter, _quote, _escape, _skipInitialSpace, _doubleQuote, _comment, _prefixArray)
            = (ctx, controller, lineTerminator[0], delimiter, quote, escape, skipInitialSpace, doubleQuote, comment, prefixArray);

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
            _controller.SwitchToLineTerminator(ParserState.Record);
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
